using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace OpaGachi
{
    // Owns the pets: routes agent events to them, lays them out on screen, decides where they may show
    // (visibility mode) and drives the frame loop.
    sealed class PetManager : IDisposable
    {
        const double LivenessEvery = 5, ForegroundEvery = 0.25, TopmostEvery = 5, DesktopZEvery = 0.5;
        const double ReferenceHeight = 160;   // "Normal" pet height in screen pixels (before the standing boost)

        readonly Settings settings;
        readonly Dictionary<string, AgentInfo> agents;
        readonly string agentsPath;
        readonly SpriteLibrary library;
        public SpriteLibrary Library { get { return library; } }   // the Agents page previews each pet
        readonly EventServer server;
        readonly List<Pet> pets = new List<Pet>();
        readonly HashSet<string> dismissed = new HashSet<string>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Random random = new Random();
        readonly Timer timer = new Timer();
        double lastTick, nextLiveness, nextForeground, nextTopmost, nextDesktopZ, nextState, nextPerf = 60, nextCompact = 30;
        long perfTicks, perfBusy, perfFrames;   // frame-loop cost, logged once a minute
        bool layoutDirty;
        Demo demo;

        // Geometry shared by all pets (pet size and standing boost are global settings). topPad leaves
        // room above the figure for hops and stretch; footPad room under the shoes for the ground decor.
        float baseScale, boost;
        int areaWidth, areaHeight, figureWidth, topPad, footPad;
        Visibility appliedVisibility;

        public PetManager(Settings settings, Dictionary<string, AgentInfo> agents, string agentsPath, SpriteLibrary library, EventServer server)
        {
            this.settings = settings;
            this.agents = agents;
            this.agentsPath = agentsPath;
            this.library = library;
            this.server = server;
            ComputeGeometry();
            appliedVisibility = settings.Visibility;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) => layoutDirty = true;   // a display added, removed or turned
            timer.Interval = 40;
            timer.Tick += (s, e) => Tick();
            timer.Start();
        }

        double Now { get { return clock.Elapsed.TotalSeconds; } }
        public int Count { get { return pets.Count(p => !p.Closing); } }
        public IEnumerable<Pet> Live { get { return pets.Where(p => !p.Closing); } }
        public Action OpenSettings;   // set by the tray app (pet right-click menu)
        public Action<int> OpenSettingsPage;
        public Action<bool> SwitchTheme;   // dev aid: {"dark":true} on /settings switches an open Settings window the way its dropdown does
        public Dictionary<string, AgentInfo> Agents { get { return agents; } }

        public void SetAnchor(Anchor anchor)
        {
            settings.Anchor = anchor;
            settings.Save();
            layoutDirty = true;
        }

        public void SaveAgents() { Settings.SaveAgents(agentsPath, agents); }

        // Re-applies everything from `settings` and `agents` to the running pets (called live by Settings):
        // names, ties, on/off, timings, size, standing boost, visibility.
        public void ApplyAll()
        {
            foreach (var pet in pets.Where(p => !p.Closing).ToList())
            {
                AgentInfo info;
                agents.TryGetValue(pet.AgentKey, out info);
                if (info != null && !info.Enabled) { Close(pet); continue; }
                pet.SetLook(info);
                string baseName = info != null ? info.Name : pet.AgentKey;
                pet.SetName(pet.Number == 1 ? baseName : baseName + " " + pet.Number);
                if (pet.InstanceKey == Demo.Key) continue;   // the demo keeps its short bored timer
                pet.BoredAfter = settings.BoredAfterSeconds;
                pet.OccasionalEvery = settings.OccasionalEverySeconds;
                pet.OccasionalChance = settings.OccasionalChance;
            }
            ComputeGeometry();
            foreach (var pet in pets) ApplyGeometry(pet);
            if (appliedVisibility != settings.Visibility)
            {
                appliedVisibility = settings.Visibility;
                nextForeground = nextDesktopZ = nextTopmost = 0;   // re-evaluate z-order and hiding now
            }
            layoutDirty = true;
        }

        void ComputeGeometry()
        {
            baseScale = (float)(settings.SizeFactor * ReferenceHeight / library.Height);
            boost = (float)Settings.StandingBoost;
            areaWidth = (int)Math.Round(library.Width * baseScale * (1 + boost));
            areaHeight = (int)Math.Round(library.Height * baseScale * (1 + boost));
            figureWidth = (int)Math.Round(library.Width * baseScale);
            topPad = (int)Math.Ceiling(20 * settings.SizeFactor);
            footPad = (int)Math.Ceiling(10 * settings.SizeFactor);
        }

        bool TopRow
        {
            get { var a = settings.Anchor; return !settings.CustomPlacement && (a == Anchor.TopLeft || a == Anchor.TopCenter || a == Anchor.TopRight); }
        }

        // The display the pets live on (Settings → Pets → Placement → Display); the main one if it's unplugged.
        public Screen TargetScreen()
        {
            return Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Display) ?? Screen.PrimaryScreen;
        }

        // For the custom location editor: one idle figure at the current pet size, the step between pets, and
        // how many stand in the row (at least three, so the line reads as a row).
        public System.Drawing.Bitmap RowPreview(out int step, out int count)
        {
            step = (int)Math.Round(figureWidth * settings.Spacing);
            count = Math.Max(3, pets.Count(p => !p.Closing && !p.InstanceKey.EndsWith(":preview", StringComparison.Ordinal)));
            bool tinted;
            var player = new ClipPlayer();
            player.Start(library.Pick(SpriteLibrary.Template, Anim.Idle, null, out tinted));
            player.SeekTo(0);
            var clip = player.Clip;
            using (var frame = new System.Drawing.Bitmap(clip.Width, clip.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                var bits = frame.LockBits(new System.Drawing.Rectangle(0, 0, clip.Width, clip.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                System.Runtime.InteropServices.Marshal.Copy(player.Frame, 0, bits.Scan0, player.Frame.Length);
                frame.UnlockBits(bits);
                var scaled = new System.Drawing.Bitmap(figureWidth, Math.Max(1, (int)Math.Round(clip.Height * baseScale)), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = System.Drawing.Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(frame, 0, 0, scaled.Width, scaled.Height);
                }
                return scaled;
            }
        }

        void ApplyGeometry(Pet pet)
        {
            pet.Window.SetInteractive(settings.ClickPets);
            pet.Window.Configure(areaWidth, areaHeight, topPad, footPad);
            pet.BaseScale = baseScale;
            pet.StandingBoost = boost;
            pet.Window.BadgeScale = (float)settings.SizeFactor;
            pet.Motion.Size = (float)settings.SizeFactor;
            pet.Motion.Top = TopRow;
            pet.ForceRedraw = true;
            pet.DrawnX = int.MinValue;   // force a present at the new size
        }

        // ---- Frame loop ----------------------------------------------------------------------

        void Tick()
        {
            long started = clock.ElapsedTicks;
            double now = Now, dt = Math.Min(0.25, now - lastTick);
            lastTick = now;

            AgentEvent ev;
            while (server.Queue.TryDequeue(out ev))
            {
                try { Route(ev, now); }
                catch (Exception e) { Log.Write("event " + ev.Path + " failed: " + e); }
            }
            SourceEvent sev;
            while (Sources.Queue.TryDequeue(out sev))
            {
                try { RouteSource(sev, now); }
                catch (Exception e) { Log.Write("source " + sev.Origin + " " + sev.Instance + " failed: " + e); }
            }
            if (demo != null && !demo.Step(this, now)) demo = null;
            if (now >= nextForeground) { nextForeground = now + ForegroundEvery; CheckForeground(now); }
            if (now >= nextLiveness) { nextLiveness = now + LivenessEvery; CheckAlive(); }
            if (layoutDirty) Layout(now);

            bool desktopMode = settings.Visibility == Visibility.DesktopOnly;
            bool fixZ = desktopMode ? now >= nextDesktopZ : now >= nextTopmost;
            if (fixZ)
            {
                if (desktopMode) nextDesktopZ = now + DesktopZEvery;
                else nextTopmost = now + TopmostEvery;
            }
            IntPtr desktop = fixZ && desktopMode ? DesktopWindow() : IntPtr.Zero;

            bool animating = false;
            foreach (var pet in pets.ToList())
            {
                pet.Motion.SetBadge(settings.Badges ? pet.Badge : null, now);
                bool hovered = (pet.Window.Hovered || pet.Motion.HoverOverride) && settings.ClickPets;
                if (hovered && pet.Motion.Hover == 0) pet.Fx.Interaction(false, now);   // configurable interaction sparkles
                pet.Motion.StepHover(hovered, dt);
                pet.Window.ShowTag(pet.Name, pet.StateWord, settings.HoverInfo ? pet.Motion.Hover : 0);
                pet.StepMotion(now);
                var pose = pet.Evaluate(now);
                bool invisible = pose.Alpha <= 0.002 && !pet.Motion.Visible;
                bool redraw = pet.Update(now, !invisible, pose);
                animating |= pose.Animated;
                // Hidden pets don't catch clicks (below 15% opacity), so they never block what's under them.
                pet.Window.SetInteractive(settings.ClickPets && pose.Alpha >= 0.15f);
                int x = (int)Math.Round(pet.X), y = (int)Math.Round(pet.Y + pose.WinDy), opacity = (int)Math.Round(Math.Max(0, Math.Min(1, pose.Alpha)) * 255);
                bool moved = x != pet.DrawnX || y != pet.DrawnY || opacity != pet.DrawnOpacity;
                if (redraw) perfFrames++;
                if (redraw || moved)
                {
                    pet.Window.Present(x, y, (byte)opacity);
                    pet.DrawnX = x;
                    pet.DrawnY = y;
                    pet.DrawnOpacity = opacity;
                }
                if (fixZ)
                {
                    if (desktopMode) pet.Window.KeepOnDesktop(desktop);
                    else pet.Window.KeepOnTop();
                }
                if (pet.Closing && pet.Motion.Gone(now)) Remove(pet);
                else if (!pet.Closing && pet.NeedsWindow && !pet.HasWindow && !pet.IsBusy && pet.ShowsMode
                         && pet.ClipElapsed(now) >= (pet.Mode == Mode.Complete ? 5 : 1) && now - pet.WindowGoneAt > 0.5)
                {
                    Log.Write(pet.InstanceKey + ": app window gone and nothing running");
                    Close(pet);   // its app was closed and it has nothing left to show
                }
            }

            // 30 fps while something moves (hops, rings, glides), back to 25 fps at rest; the sprite itself
            // only redraws at its own 12 fps.
            // The clips run at 24 fps (2026-11-02): tick about every 16 ms while a pet shows, so each frame is caught
            // on time instead of some lasting a tick longer than others; nothing is drawn unless a frame changed.
            int interval = pets.Any(p => !p.Closing && p.Motion.Visible) ? 15 : 40;
            if (timer.Interval != interval) timer.Interval = interval;
            if (now >= nextState) { nextState = now + 0.25; PublishState(); }

            // Clip arrays are large-object-heap allocations; compact after evictions so the working set
            // doesn't creep up as pets cycle through animations (at most every 30 s).
            if (now >= nextCompact && library.Evictions > 0)
            {
                nextCompact = now + 30;
                library.Evictions = 0;
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
            }

            perfTicks++;
            perfBusy += clock.ElapsedTicks - started;
            if (now >= nextPerf)
            {
                double busyMs = perfBusy * 1000.0 / Stopwatch.Frequency;
                Log.Write(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "perf: {0} pets, {1:F1} frames/s, frame loop busy {2:F1} ms/s ({3:F3}% of one core), {4:F2} ms per tick, working set {5:F1} MB",
                    Count, perfFrames / 60.0, busyMs / 60.0, busyMs / 600.0, busyMs / perfTicks, Environment.WorkingSet / 1048576.0));
                Log.Write(string.Format(System.Globalization.CultureInfo.InvariantCulture, "perf: decode {0:F1} ms/s, draw {1:F1} ms/s",
                    Pet.DecodeTicks * 1000.0 / Stopwatch.Frequency / 60, Pet.DrawTicks * 1000.0 / Stopwatch.Frequency / 60));
                double f = 1000.0 / Stopwatch.Frequency / 60;
                Log.Write(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "perf: sources (background threads) claude {0:F2} ms/s, codex {1:F2} ms/s, apps {2:F2} ms/s",
                    System.Threading.Interlocked.Exchange(ref Sources.ClaudeTicks, 0) * f,
                    System.Threading.Interlocked.Exchange(ref Sources.CodexTicks, 0) * f,
                    System.Threading.Interlocked.Exchange(ref Sources.AppTicks, 0) * f));
                Pet.DecodeTicks = Pet.DrawTicks = 0;
                nextPerf = now + 60;
                perfTicks = perfBusy = perfFrames = 0;
            }
        }

        // ---- Events --------------------------------------------------------------------------

        void Route(AgentEvent ev, double now)
        {
            if (ev.Path == GrokHooks.HookPath || (ev.Path == ClaudeCode.HookPath && GrokHooks.IsGrokPayload(ev.Body)))
            {
                // Grok Build: its own hook file, or Claude Code's hooks (which Grok Build also runs).
                if (ev.Path == ClaudeCode.HookPath && GrokHooks.Installed) return;   // its own file reports it already
                string key = GrokHooks.InstanceKey(ev.Body);
                if (key == null) return;
                var pet = Find(key);
                bool closed;
                var signals = GrokHooks.Map(ev.Body, pet != null && pet.Mode == Mode.Help, out closed);
                Log.Write("grok " + Json.Str(ev.Body, "hookEventName", "?") + " " + Json.Str(ev.Body, "toolName", "") + " -> " + (closed ? "close" : string.Join(",", signals)));
                if (closed) { if (pet != null) Close(pet); return; }
                if (signals.Count == 0) return;
                if (pet == null) pet = Spawn(GrokHooks.Agent, key, now);
                if (pet == null) return;
                pet.Label = "Grok Build";
                foreach (var s in signals) pet.Handle(s, now);
            }
            else if (ev.Path == GeminiHooks.HookPath)
            {
                // {"event","session","tool","notification","error","host","pid"} from OpaGachi.exe --gemini-hook
                string session = Json.Str(ev.Body, "session", "");
                if (session.Length == 0) return;
                string key = "gemini-cli:" + session, what = Json.Str(ev.Body, "event", "");
                var pet = Find(key);
                if (what == "SessionEnd") { if (pet != null) Close(pet); return; }
                if (pet == null) pet = Spawn(GeminiHooks.Agent, key, now);
                if (pet == null) return;
                pet.Label = "Gemini CLI";
                long host = (long)Json.Num(ev.Body, "host", 0);
                if (host != 0) pet.HostWindow = new IntPtr(host);
                int pid = (int)Json.Num(ev.Body, "pid", 0);
                if (pid > 0) pet.AgentPid = pid;
                bool waiting = pet.Mode == Mode.Help;
                var signals = new List<Signal>();
                switch (what)
                {
                    case "SessionStart": signals.Add(Signal.Opened); break;
                    case "BeforeAgent": signals.Add(Signal.UserPrompted); break;
                    case "BeforeTool":
                        if (waiting) signals.Add(Signal.Approved);
                        signals.Add(GeminiHooks.ClassifyTool(Json.Str(ev.Body, "tool", "")));
                        break;
                    case "AfterTool": signals.Add(Json.Bool(ev.Body, "error") ? Signal.Error : Signal.Think); break;
                    case "Notification": if (Json.Str(ev.Body, "notification", "") == "ToolPermission") signals.Add(Signal.NeedsApproval); break;
                    case "AfterAgent": signals.Add(Signal.Done); break;
                }
                Log.Write("gemini-cli " + what + " " + Json.Str(ev.Body, "tool", "") + " -> " + string.Join(",", signals));
                foreach (var s in signals) pet.Handle(s, now);
            }
            else if (ev.Path == StatusLineRelay.Path_)
            {
                ClaudeUsage.FromStatusLine(ev.Body);
            }
            else if (ev.Path == ClaudeCode.HookPath)
            {
                string key = ClaudeCode.InstanceKey(ev.Body);
                if (key == null || !ClaudeSessions.IsLive(Json.Str(ev.Body,"session_id",""))) return;
                var pet = Find(key);
                bool closed;
                var signals = ClaudeCode.Map(ev.Body, pet != null && pet.Mode == Mode.Help, out closed);
                Log.Write("claude-code " + Json.Str(ev.Body, "hook_event_name", "?") + " " + Json.Str(ev.Body, "tool_name", "")
                          + " -> " + (closed ? "close" : string.Join(",", signals)));
                if (closed) { if (pet != null) Close(pet); return; }
                if (signals.Count == 0) return;
                if (pet == null) pet = Spawn(ClaudeCode.Agent, key, now);
                if (pet == null) return;   // Claude is switched off in Settings
                pet.HooksSeen = true;
                foreach (var s in signals) pet.Handle(s, now);
            }
            else if (ev.Path == ClaudeCode.HostPath)
            {
                bool grok = Json.Str(ev.Body, "agent", ClaudeCode.Agent) == GrokHooks.Agent;
                string key = grok ? "grok-build:" + Json.Str(ev.Body, "session_id", "") : ClaudeCode.InstanceKey(ev.Body);
                if (key == null || (!grok && !ClaudeSessions.IsLive(Json.Str(ev.Body,"session_id","")))) return;
                var pet = Find(key) ?? Spawn(grok ? GrokHooks.Agent : ClaudeCode.Agent, key, now);
                if (pet == null) return;
                pet.HostWindow = new IntPtr((long)Json.Num(ev.Body, "host_hwnd", 0));
                pet.AgentPid = (int)Json.Num(ev.Body, "agent_pid", 0);
            }
            else if (ev.Path == CodexHooks.HookPath)
            {
                // {"event":"PermissionRequest","instance":"app:chatgpt","tool":"..."} from OpaGachi.exe --codex-hook
                string key = Json.Str(ev.Body, "instance", CodexWatcher.CliInstance);
                Log.Write("codex hook " + Json.Str(ev.Body, "event", "?") + " " + Json.Str(ev.Body, "tool", "") + " -> " + key);
                if (Json.Str(ev.Body, "event", "") != "PermissionRequest") return;
                var pet = Find(key) ?? Spawn("chatgpt", key, now);
                if (pet != null) pet.Handle(Signal.NeedsApproval, now);
            }
            else if (ev.Path == "/settings")
            {
                // Scripted settings changes (same path the Settings window uses):
                // {"size":0..4, "visibility":"DesktopOnly", "anchor":"BottomRight", "enable":{"chatgpt":false}}
                // {"page":0..4} opens Settings on that page (dev aid: screenshots of page switches).
                object v;
                if (ev.Body.TryGetValue("page", out v) && OpenSettingsPage != null) { OpenSettingsPage(Convert.ToInt32(v)); return; }
                if (ev.Body.TryGetValue("dark", out v) && v is bool && SwitchTheme != null) { SwitchTheme((bool)v); return; }
                if (ev.Body.TryGetValue("size", out v)) settings.Size = Math.Max(0, Math.Min(Settings.SizeFactors.Length - 1, Convert.ToInt32(v)));
                Visibility visibility;
                if (Enum.TryParse(Json.Str(ev.Body, "visibility", ""), true, out visibility)) settings.Visibility = visibility;
                Anchor anchor;
                if (Enum.TryParse(Json.Str(ev.Body, "anchor", ""), true, out anchor)) settings.Anchor = anchor;
                var enable = ev.Body.TryGetValue("enable", out v) ? v as IDictionary<string, object> : null;
                if (enable != null)
                {
                    foreach (var kv in enable)
                    {
                        AgentInfo info;
                        if (agents.TryGetValue(kv.Key, out info) && kv.Value is bool) info.Enabled = (bool)kv.Value;
                    }
                    SaveAgents();
                }
                settings.Save();
                ApplyAll();
                if (ev.Body.TryGetValue("preview", out v) && v is bool)
                {
                    if ((bool)v) PreviewAll();
                    else ClearPreview();
                }
                Log.Write("settings via api: size=" + settings.Size + " visibility=" + settings.Visibility + " anchor=" + settings.Anchor);
            }
            else if (ev.Path == "/signal")
            {
                // Generic adapter/test endpoint:
                // {"agent":"claude","instance":"x","signal":"Work"} | {"anim":"Thank"} | {"close":true}
                // Choreography checks without touching the real mouse: {"hover":true|false} | {"click":true}
                string agent = Json.Str(ev.Body, "agent", "demo");
                string key = agent + ":" + Json.Str(ev.Body, "instance", "default");
                var pet = Find(key);
                if (Json.Bool(ev.Body, "close")) { if (pet != null) Close(pet); return; }
                if (pet == null) pet = Spawn(agent, key, now);
                if (pet == null) return;
                object flag;
                if (ev.Body.TryGetValue("hover", out flag) && flag is bool) pet.Motion.HoverOverride = (bool)flag;
                if (Json.Bool(ev.Body, "click")) pet.Motion.Click(now);
                string snapshot = Json.Str(ev.Body, "snapshot", null);
                if (snapshot != null) { try { pet.Window.Snapshot(snapshot); } catch (Exception e) { Log.Write("snapshot: " + e.Message); } }
                Signal signal;
                Anim anim;
                if (Enum.TryParse(Json.Str(ev.Body, "signal", ""), true, out signal)) pet.Handle(signal, now);
                if (Enum.TryParse(Json.Str(ev.Body, "anim", ""), true, out anim)) pet.Force(anim, now);
            }
        }

        // Events from the background activity sources (Claude Code sessions, Codex threads, desktop apps).
        void RouteSource(SourceEvent ev, double now)
        {
            if (dismissed.Contains(ev.Instance))
            {
                if (ev.Present == false) dismissed.Remove(ev.Instance);
                return;
            }
            var pet = Find(ev.Instance);
            if (ev.Present.HasValue)
            {
                if (ev.Present.Value)
                {
                    if (pet == null) pet = Spawn(ev.Agent, ev.Instance, now);
                    if (pet == null) return;   // agent switched off
                    if (ev.Host != IntPtr.Zero) pet.HostWindow = ev.Host;
                    if (ev.NeedsWindow) pet.HasWindow = true;
                    else if (ev.Pid > 0) pet.AgentPid = ev.Pid;
                    if (ev.Label != null) pet.Label = ev.Label;
                }
                else if (pet != null)
                {
                    if (ev.NeedsWindow)
                    {
                        pet.HasWindow = false;   // leaves once idle (checked every frame)
                        pet.WindowGoneAt = now;
                        pet.HostWindow = IntPtr.Zero;
                    }
                    else Close(pet);
                }
                return;
            }
            if (ev.Origin == ClaudeSessions.Origin && pet != null && pet.HooksSeen) return;   // hooks are exact; skip the transcript
            if (pet == null) pet = Spawn(ev.Agent, ev.Instance, now);
            if (pet == null) return;
            var before = pet.Mode;
            foreach (var signal in ev.Signals) pet.Handle(signal, now);
            if (pet.Mode != before) Log.Write(pet.InstanceKey + ": " + before + " -> " + pet.Mode + " (" + ev.Origin + ": " + string.Join(",", ev.Signals) + ")");
        }

        // Left-click on a pet: bring its agent's window (app or terminal) to the front.
        void FocusAgent(Pet pet)
        {
            IntPtr host = pet.HostWindow;
            if (host == IntPtr.Zero || !Native.IsWindow(host))
            {
                Log.Write("click " + pet.InstanceKey + ": no window to show");
                return;
            }
            if (Native.IsIconic(host)) Native.ShowWindow(host, Native.SW_RESTORE);
            Native.SetForegroundWindow(host);   // allowed: we just received the click
            Log.Write("click " + pet.InstanceKey + ": showed '" + Native.WindowText(host) + "'");
        }

        void ShowPetMenu(Pet pet)
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.Ui(9F) };
            MenuFade.Attach(menu);
            var title = new ToolStripLabel(pet.Name + " · " + pet.StateWord + (string.IsNullOrEmpty(pet.Label) ? "" : " (" + pet.Label + ")")) { ForeColor = Theme.TextSecondary };
            menu.Items.Add(title);
            menu.Items.Add(new ToolStripSeparator());
            var show = new ToolStripMenuItem("Show " + (string.IsNullOrEmpty(pet.Label) ? "its window" : pet.Label)) { Enabled = pet.HostWindow != IntPtr.Zero };
            show.Click += (s, e) => FocusAgent(pet);
            menu.Items.Add(show);
            var close = new ToolStripMenuItem("Close Window");
            close.Click += (s,e) =>
            {
                using (var confirm = new ConfirmForm("Are you sure you want to close this window?", "Confirm"))
                    if (confirm.ShowDialog() == DialogResult.OK) { dismissed.Add(pet.InstanceKey); Close(pet); }
            };
            menu.Items.Add(close);
            AgentInfo info;
            if (agents.TryGetValue(pet.AgentKey, out info) && !info.Hidden)
            {
                var hide = new ToolStripMenuItem("Turn off " + info.Name + " pets");
                hide.Click += (s, e) => { info.Enabled = false; SaveAgents(); ApplyAll(); };
                menu.Items.Add(hide);
            }
            menu.Items.Add(new ToolStripSeparator());
            var open = new ToolStripMenuItem("Settings...");
            open.Click += (s, e) => { if (OpenSettings != null) OpenSettings(); };
            menu.Items.Add(open);
            menu.Closed += (s, e) => menu.BeginInvoke((Action)menu.Dispose);
            menu.Show(Cursor.Position);
        }

        public Pet Find(string key)
        {
            return pets.FirstOrDefault(p => p.InstanceKey == key && !p.Closing);
        }

        // Returns null when the agent is switched off in Settings.
        public Pet Spawn(string agent, string key, double now)
        {
            AgentInfo info;
            agents.TryGetValue(agent, out info);
            if (dismissed.Contains(key) || (info != null && !info.Enabled)) return null;
            var pet = new Pet(agent, key, info, library, random, now, areaWidth, areaHeight, settings.ClickPets)
            {
                BoredAfter = settings.BoredAfterSeconds,
                OccasionalEvery = settings.OccasionalEverySeconds,
                OccasionalChance = settings.OccasionalChance,
            };
            ApplyGeometry(pet);
            var taken = new HashSet<int>(pets.Where(p => p.AgentKey == agent && !p.Closing).Select(p => p.Number));
            pet.Number = 1;
            while (taken.Contains(pet.Number)) pet.Number++;
            string baseName = info != null ? info.Name : agent;
            pet.SetName(pet.Number == 1 ? baseName : baseName + " " + pet.Number);
            pet.NeedsWindow = key.StartsWith("app:", StringComparison.Ordinal);
            pet.Window.PetClicked += (s, e) => { pet.Fx.Interaction(true, Now); FocusAgent(pet); };
            pet.Window.PetMenu += (s, e) => ShowPetMenu(pet);
            pet.Window.HoverChanged += (s, e) => pet.ForceRedraw = true;
            pet.Completed = p =>
            {
                if (p.InstanceKey.EndsWith(":preview", StringComparison.Ordinal)) return;
                if (settings.Sound) Sound.Play(settings.SoundFile);
                if (p.AgentKey != Demo.Agent) AnswerLog.Record(p.AgentKey);   // Usage page: answers per day
            };
            pet.Window.PetPressed += (s, e) => pet.Motion.Press(Now);
            pet.Window.PetReleased += (s, e) => pet.Motion.Release(Now);
            pets.Add(pet);
            Layout(now, pet);
            pet.Window.Show();
            pet.Update(now);
            pet.Window.Present((int)pet.X, (int)pet.Y, 0);
            nextForeground = nextDesktopZ = nextTopmost = 0;   // apply hiding / z-order to the newcomer right away
            Log.Write("spawn " + key + " as '" + pet.Name + "'");
            return pet;
        }

        // Departure (Gemini Response 3): the outermost pet lifts 4 px then sinks away (360 ms); one from the
        // middle sinks over an imploding ring (320 ms) and its neighbours start closing the gap at once
        // (Gemini Response 5: they don't wait for it to vanish).
        public void Close(Pet pet)
        {
            if (pet.Closing) return;
            double now = Now;
            var row = pets.Where(p => !p.Closing).ToList();
            bool endOfRow = row.Count == 0 || row[row.Count - 1] == pet;
            pet.Closing = true;
            pet.Motion.Leave(now, endOfRow);
            layoutDirty = true;
            Log.Write("close " + pet.InstanceKey);
        }

        void Remove(Pet pet)
        {
            pets.Remove(pet);
            pet.Window.Close();
            pet.Window.Dispose();
            layoutDirty = true;
        }

        // ---- Foreground: "viewed" detection and visibility rules --------------------------------

        readonly Dictionary<uint, string> processNames = new Dictionary<uint, string>();
        double processNamesClearedAt;
        string foregroundInfo = "";   // "process | title" of the window in front, for /state diagnostics

        void CheckForeground(double now)
        {
            IntPtr fg = Native.GetForegroundWindow();
            IntPtr root = fg == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(fg, Native.GA_ROOT);
            IntPtr owner = fg == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(fg, Native.GA_ROOTOWNER);
            Func<IntPtr, bool> isFront = h => h != IntPtr.Zero && (h == fg || h == root || h == owner);

            foreach (var pet in pets)
            {
                if (pet.Closing || pet.Mode != Mode.Complete || pet.HostWindow == IntPtr.Zero) continue;
                if (isFront(pet.HostWindow)) pet.Handle(Signal.Viewed, now);
            }

            string process = "", title = "";
            if (fg != IntPtr.Zero)
            {
                process = ProcessName(root != IntPtr.Zero ? root : fg);
                title = Native.WindowText(root != IntPtr.Zero ? root : fg);
            }
            foregroundInfo = process + " | " + title;
            bool anyAiInFront = settings.Visibility == Visibility.AllButAiApps
                && (agents.Values.Any(a => !a.Hidden && BelongsTo(a, process, title)) || pets.Any(p => isFront(p.HostWindow)));

            foreach (var pet in pets)
            {
                if (pet.Closing) continue;
                bool hide;
                switch (settings.Visibility)
                {
                    case Visibility.AllButAiApps:
                        hide = anyAiInFront;
                        break;
                    case Visibility.AllButOwnApp:
                        AgentInfo info;
                        agents.TryGetValue(pet.AgentKey, out info);
                        hide = isFront(pet.HostWindow) || (info != null && BelongsTo(info, process, title));
                        break;
                    default:
                        hide = false;   // desktop only: windows simply cover the pets
                        break;
                }
                pet.Motion.SetVisible(!hide, now);
            }
        }

        // Is the foreground window (process + title) this agent's own desktop app?
        static bool BelongsTo(AgentInfo info, string process, string title)
        {
            if (process.Length > 0)
                foreach (var app in info.Apps.Concat(AppWatcher.ProcessesOf(info.Key)))
                    if (string.Equals(StripExe(app), process, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string StripExe(string name)
        {
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }

        string ProcessName(IntPtr hwnd)
        {
            if (Now - processNamesClearedAt > 30) { processNames.Clear(); processNamesClearedAt = Now; }   // pids get reused
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            string name;
            if (!processNames.TryGetValue(pid, out name))
            {
                try { name = Process.GetProcessById((int)pid).ProcessName; }
                catch (Exception) { name = ""; }
                processNames[pid] = name;
            }
            return name;
        }

        // The window that hosts the desktop icons (Progman, or a WorkerW on some setups).
        IntPtr desktopWindow;
        double desktopFoundAt = -100;

        IntPtr DesktopWindow()
        {
            if (Now - desktopFoundAt < 10 && desktopWindow != IntPtr.Zero && Native.IsWindow(desktopWindow)) return desktopWindow;
            desktopFoundAt = Now;
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, _) =>
            {
                if (Native.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
                found = hwnd;
                return false;
            }, IntPtr.Zero);
            desktopWindow = found;
            return found;
        }

        void CheckAlive()
        {
            foreach (var pet in pets.Where(p => p.AgentPid > 0 && !p.Closing).ToList())
            {
                bool alive;
                try { alive = !Process.GetProcessById(pet.AgentPid).HasExited; }
                catch (Exception) { alive = false; }
                if (!alive) { Log.Write("agent process " + pet.AgentPid + " exited"); Close(pet); }
            }
        }

        // Pets sit in a row from the chosen anchor, on the primary monitor's work area
        // (so "bottom" means just above the taskbar wherever it is). Spacing and margins are measured
        // on the figure, not the window, which is wider to leave room for the standing boost.
        // Pets glide on the Smooth spring (Gemini Response 5), so a pet already moving just retargets. Gaps
        // close one pet after another, nearest the gap first (50 ms apart), like a row of people shuffling
        // along; each sets off with a small step-hop. To make room for a newcomer the others glide over
        // (40 ms apart) and it rises in 140 ms later. Staggers stop growing after 240 ms.
        // The clip's bottom edge (the ground line) sits on the work area's edge; the window reaches footPad
        // past it for the shadow and rings.
        void Layout(double now, Pet arriving = null)
        {
            layoutDirty = false;
            var row = pets.Where(p => !p.Closing).ToList();
            var area = TargetScreen().WorkingArea;   // portrait or landscape, wherever its taskbar is
            int inset = (areaWidth - figureWidth) / 2;
            int step = (int)Math.Round(figureWidth * settings.Spacing), m = settings.Margin;
            int total = figureWidth + Math.Max(0, row.Count - 1) * step;
            var a = settings.Anchor;
            int y = TopRow ? area.Top + m / 2 : area.Bottom - (PetWindow.TagHeight + topPad + areaHeight);
            // Experimental custom location: the row is centred on the dragged spot, its feet on the dotted line, and
            // kept on the display.
            int customLeft = 0;
            if (settings.CustomPlacement)
            {
                y = area.Top + (int)Math.Round(settings.CustomY * area.Height) - (PetWindow.TagHeight + topPad + areaHeight);
                customLeft = Math.Max(area.Left, Math.Min(area.Right - total, area.Left + (int)Math.Round(settings.CustomX * area.Width) - total / 2));
            }
            int moved = 0;
            bool makingRoom = false;
            for (int i = 0; i < row.Count; i++)
            {
                int x;
                if (settings.CustomPlacement) x = customLeft - inset + i * step;
                else if (a == Anchor.BottomLeft || a == Anchor.TopLeft) x = area.Left + m - inset + i * step;
                else if (a == Anchor.BottomRight || a == Anchor.TopRight) x = area.Right - m - areaWidth + inset - i * step;
                else x = area.Left + (area.Width - total) / 2 - inset + i * step;
                var pet = row[i];
                if (pet == arriving) { pet.Place(x, y); continue; }
                if (pet.TargetX == x && pet.TargetY == y) continue;
                if (arriving != null)
                {
                    pet.MoveTo(x, y, now, Math.Min(0.24, 0.04 * moved++), MoveCurve.MakeRoom);
                    makingRoom = true;
                }
                else
                {
                    pet.MoveTo(x, y, now, Math.Min(0.24, 0.05 * moved++), MoveCurve.CloseGap);
                }
            }
            if (arriving != null)
            {
                arriving.Motion.Arrive(now, row.Count == 1, makingRoom ? 0.14 : 0);
                arriving.Fx.Burst(FxKind.Small, now, (makingRoom ? 0.14 : 0) + 0.2);
            }
        }

        void PublishState()
        {
            var state = new Dictionary<string, object>();
            state["visibility"] = settings.Visibility.ToString();
            state["foreground"] = foregroundInfo;
            state["pets"] = pets.Select(p => (object)new Dictionary<string, object>
            {
                { "instance", p.InstanceKey }, { "name", p.Name }, { "mode", p.Mode.ToString() },
                { "anim", p.CurrentAnim.ToString() }, { "hostWindow", p.HostWindow.ToInt64() },
                { "agentPid", p.AgentPid }, { "closing", p.Closing }, { "hidden", !p.Motion.Visible },
                { "window", areaWidth + "x" + (topPad + areaHeight + footPad + PetWindow.TagHeight) },
                { "label", p.Label ?? "" }, { "hasWindow", p.HasWindow }, { "hooks", p.HooksSeen },
                { "x", (int)p.X }, { "y", (int)p.Y }, { "hovered", p.Window.Hovered },
            }).ToList();
            state["sources"] = Sources.Status.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            server.StateJson = Json.Write(state);
        }

        // ---- Demo / debug ----------------------------------------------------------------------

        public void StartDemo()
        {
            var old = Find(Demo.Key);
            if (old != null) Close(old);
            demo = new Demo(Now);
        }

        public void StopDemo()
        {
            demo = null;
            var pet = Find(Demo.Key);
            if (pet != null) Close(pet);
        }

        // One pet per enabled agent, alternating standing and sitting poses, for judging sizes, ties
        // and the standing boost side by side. Pets use the instance key "<agent>:preview".
        public void PreviewAll()
        {
            double now = Now;
            Anim[] poses = { Anim.Idle, Anim.Work, Anim.Idle, Anim.Think, Anim.Idle };
            int i = 0;
            foreach (var info in agents.Values.Where(a => !a.Hidden && a.Enabled))
            {
                var pet = Find(info.Key + ":preview") ?? Spawn(info.Key, info.Key + ":preview", now);
                if (pet == null) continue;
                pet.Force(poses[i++ % poses.Length], now);
            }
        }

        public void ClearPreview()
        {
            foreach (var pet in pets.Where(p => p.InstanceKey.EndsWith(":preview", StringComparison.Ordinal) && !p.Closing).ToList()) Close(pet);
        }

        // Welcome's Get started: the pets hop 60 ms after the window closes (Gemini Response 3, 4.1c).
        public void Greet()
        {
            double at = Now + 0.06;
            foreach (var pet in pets.Where(p => !p.Closing)) pet.Motion.Alert(AlertKind.Greet, at);
        }

        // Experimental custom sprites on/off, or a new custom file imported: every pet re-picks its clip.
        public void RefreshSprites()
        {
            library.Custom = settings.CustomSprites;
            library.Reload();
            double now = Now;
            foreach (var pet in pets.Where(p => !p.Closing)) pet.Repick(now);
        }

        public void ForceAll(Anim anim)
        {
            double now = Now;
            if (Count == 0) Spawn(Demo.Agent, Demo.Key, now);
            foreach (var pet in pets.Where(p => !p.Closing)) pet.Force(anim, now);
        }

        public void Dispose()
        {
            timer.Stop();
            foreach (var pet in pets.ToList()) Remove(pet);
        }

        // Scripted walkthrough of the brief's example (Pong with ducks), hitting all ten animations.
        // Each step waits until the pet has actually shown the previous state for a moment: clips play to
        // the end, so the body follows the script at its own pace.
        sealed class Demo
        {
            public const string Agent = "demo";
            public const string Key = "demo:walkthrough";

            struct Beat
            {
                public Signal Signal;
                public Anim Shows;
                public double Hold;
                public Beat(Signal signal, Anim shows, double hold) { Signal = signal; Shows = shows; Hold = hold; }
            }

            static readonly Beat[] Script =
            {
                new Beat(Signal.Opened, Anim.Idle, 3),
                new Beat(Signal.UserPrompted, Anim.Think, 5),   // "code Pong with ducks"   -> Think
                new Beat(Signal.Work, Anim.Work, 5),            // writing code             -> Work
                new Beat(Signal.NeedsApproval, Anim.Help, 5),   // wants to run a command   -> Help
                new Beat(Signal.Approved, Anim.Thank, 2),       // user approves            -> Thank, Think
                new Beat(Signal.Work, Anim.Work, 4),
                new Beat(Signal.FatalError, Anim.Fail, 5),      // command fails            -> Fail
                new Beat(Signal.UserPrompted, Anim.Thank, 2),   // "try again"              -> Thank, Think
                new Beat(Signal.ImageGen, Anim.ImageGen, 5),    // makes a bread sprite     -> Image Generation
                new Beat(Signal.Done, Anim.Complete, 6),        // finished                 -> Complete (loops)
                new Beat(Signal.Viewed, Anim.Thank, 2),         // user looks               -> Thank, Idle -> occasional idles -> Bored
            };
            const double StepTimeout = 35;

            int next;
            double sentAt;

            public Demo(double now) { sentAt = now; }

            public bool Step(PetManager m, double now)
            {
                var pet = m.Find(Key);
                if (pet == null)
                {
                    pet = m.Spawn(Agent, Key, now);
                    if (pet == null) return false;
                    pet.BoredAfter = 25;     // show Bored without a 10-minute wait
                }
                if (next > 0)
                {
                    var previous = Script[next - 1];
                    bool shown = pet.CurrentAnim == previous.Shows && pet.ClipElapsed(now) >= previous.Hold;
                    if (!shown && now - sentAt < StepTimeout) return true;
                }
                if (next >= Script.Length) return false;
                pet.Handle(Script[next].Signal, now);
                sentAt = now;
                next++;
                return true;
            }
        }
    }
}
