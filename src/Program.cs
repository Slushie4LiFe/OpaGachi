using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("OpaGachi")]

namespace OpaGachi
{
    static class Program
    {
        // App folder = the folder holding assets\ (the exe lives in bin\ during development). The app only reads
        // it, so an install or update can replace it whole.
        public static string Root { get; private set; }
        // User data: settings, agents, log, hook paths, custom sprites, uploaded ties and sounds.
        public static string Data { get; private set; }
        public static string ExePath { get { return Application.ExecutablePath; } }
        public static string[] Args = new string[0];

        [STAThread]
        static int Main(string[] args)
        {
            Args = args;
            Root = FindRoot();
            Data = FindData();
            CopyLegacyData();
            Directory.CreateDirectory(Data);
            Log.Init(Path.Combine(Data, "opagachi.log"));
            var settings = Settings.Load(Path.Combine(Data, "settings.json"));
            Theme.Apply(settings.Dark, settings.HighContrast, settings.TextScale);

            if (args.Contains("--hook-hostinfo")) return ClaudeCode.RunHostInfo(settings.Port);
            if (args.Contains("--gemini-hook")) return GeminiHooks.RunHook(settings.Port);
            int relayArg = Array.IndexOf(args, "--statusline");
            if (relayArg >= 0) return StatusLineRelay.Run(settings.Port, relayArg + 1 < args.Length ? args[relayArg + 1] : "-");
            string hookTarget;
            if (Flag(args, "--install-gemini-hook", GeminiHooks.DefaultPath(), out hookTarget)) { GeminiHooks.Install(hookTarget, Application.ExecutablePath); return 0; }
            if (Flag(args, "--uninstall-gemini-hook", GeminiHooks.DefaultPath(), out hookTarget)) { GeminiHooks.Uninstall(hookTarget); return 0; }
            if (Flag(args, "--install-statusline", ClaudeCode.DefaultSettingsPath(), out hookTarget)) { StatusLineRelay.Install(hookTarget, Application.ExecutablePath); return 0; }
            if (Flag(args, "--uninstall-statusline", ClaudeCode.DefaultSettingsPath(), out hookTarget)) { StatusLineRelay.Uninstall(hookTarget); return 0; }
            if (args.Contains("--install-grok-hook")) { GrokHooks.Install(settings.Port); return 0; }
            if (args.Contains("--uninstall-grok-hook")) { GrokHooks.Uninstall(); return 0; }
            if (args.Contains("--remove-hooks")) { FactoryReset.RemoveHooks(Root, Data); return 0; }   // the uninstaller runs this first
            int hookArg = Array.IndexOf(args, "--codex-hook");
            if (hookArg >= 0) return CodexHooks.RunHook(settings.Port, hookArg + 1 < args.Length ? args[hookArg + 1] : null);
            if (args.Contains("--install-codex-hook"))
            {
                CodexHooks.Install(CodexHooks.DefaultPath(), Application.ExecutablePath);
                Console.WriteLine("Installed the OpaGachi approval hook into " + CodexHooks.DefaultPath() + ". Trust it in Codex with /hooks.");
                return 0;
            }
            if (args.Contains("--uninstall-codex-hook"))
            {
                CodexHooks.Uninstall(CodexHooks.DefaultPath());
                Console.WriteLine("Removed the OpaGachi hook from " + CodexHooks.DefaultPath());
                return 0;
            }
            int pidArg = Array.IndexOf(args, "--host-of"), hostPid;
            if (pidArg >= 0 && pidArg + 1 < args.Length && int.TryParse(args[pidArg + 1], out hostPid)) return ClaudeCode.RunHostOf(hostPid);
            int i = Array.IndexOf(args, "--install-claude-hooks");
            if (i >= 0)
            {
                string target = i + 1 < args.Length ? args[i + 1] : ClaudeCode.DefaultSettingsPath();
                ClaudeCode.InstallHooks(target, settings.Port, Application.ExecutablePath);
                Console.WriteLine("Installed OpaGachi hooks into " + target);
                return 0;
            }
            i = Array.IndexOf(args, "--uninstall-claude-hooks");
            if (i >= 0)
            {
                string target = i + 1 < args.Length ? args[i + 1] : ClaudeCode.DefaultSettingsPath();
                ClaudeCode.UninstallHooks(target);
                Console.WriteLine("Removed OpaGachi hooks from " + target);
                return 0;
            }

            int importArg = Array.IndexOf(args, "--import-sprite");   // self-check for the custom sprite import: prints the frame count
            if (importArg >= 0 && importArg + 1 < args.Length)
            {
                var lib = new SpriteLibrary(Path.Combine(Path.Combine(Root, "assets"), "sprites"));
                try { Console.WriteLine("imported " + CustomSprite.Import(args[importArg + 1], lib.Root, lib.Width, lib.Height) + " frames"); return 0; }
                catch (Exception e) { Console.WriteLine("import failed: " + e.Message); return 1; }
            }

            int resetAt = Array.IndexOf(args, "--restart-after"), resetPid;
            if (resetAt >= 0 && resetAt + 1 < args.Length && int.TryParse(args[resetAt + 1], out resetPid))
            {
                try { using (var old = Process.GetProcessById(resetPid)) if (!old.WaitForExit(15000)) return 1; }
                catch (ArgumentException) { }
            }
            bool first;
            using (new Mutex(true, "Local\\OpaGachi.SingleInstance", out first))
            {
                if (!first) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("UI thread exception: " + e.Exception);
                try
                {
                    Application.Run(new TrayApp(settings, args.Contains("--demo"), args.Contains("--settings") ? PageArg(args) : -1, args.Contains("--menu")));
                }
                catch (Exception e)
                {
                    Log.Write("fatal: " + e);
                    MessageBox.Show(e.Message, "OpaGachi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
            return 0;
        }

        // "--flag [path]": true when present; `target` is the path after it or the default.
        static bool Flag(string[] args, string flag, string fallback, out string target)
        {
            int i = Array.IndexOf(args, flag);
            target = i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : fallback;
            return i >= 0;
        }

        // "--page N" picks the Settings page opened by --settings (dev aid for screenshots).
        static int PageArg(string[] args)
        {
            int i = Array.IndexOf(args, "--page"), page;
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out page) ? page : 0;
        }

        static string FindRoot()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            for (int up = 0; up < 3 && dir != null; up++)
            {
                if (Directory.Exists(Path.Combine(dir, "assets"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return Path.GetDirectoryName(Application.ExecutablePath);
        }

        // Starts a fresh copy that waits for this one to exit (the single-instance mutex), then quits.
        public static void Restart(string args)
        {
            Process.Start(new ProcessStartInfo(ExePath, "--restart-after " + Process.GetCurrentProcess().Id + " " + args) { UseShellExecute = false });
            Application.Exit();
        }

        // After hooks are added or removed (user, 2026-11-02): restart now, or later.
        public static bool AskRestart(IWin32Window owner, string args)
        {
            using (var ask = new ConfirmForm("For pets to work properly, OpaGachi needs to restart. Restart now?", "Restart", "Later"))
            {
                if (ask.ShowDialog(owner) != DialogResult.OK) return false;
                Restart(args);
                return true;
            }
        }

        // %LOCALAPPDATA%\OpaGachi, outside the install folder so updates never touch it. OPAGACHI_HOME (tests)
        // stands in for the user profile here as it does for the agent folders.
        static string FindData()
        {
            string home = Environment.GetEnvironmentVariable("OPAGACHI_HOME");
            string local = string.IsNullOrEmpty(home) ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : Path.Combine(Path.Combine(home, "AppData"), "Local");
            return Path.Combine(local, "OpaGachi");
        }

        // Before 1.0 the user's data lived in the app folder. Copied over once; the originals stay as a backup.
        static void CopyLegacyData()
        {
            if (Directory.Exists(Data) || !File.Exists(Path.Combine(Root, "settings.json"))) return;
            Directory.CreateDirectory(Data);
            foreach (var name in new[] { "settings.json", "hook-paths.json" })
                if (File.Exists(Path.Combine(Root, name))) File.Copy(Path.Combine(Root, name), Path.Combine(Data, name), true);
            string agents = Path.Combine(Path.Combine(Root, "assets"), "agents.json");
            if (File.Exists(agents)) File.Copy(agents, Path.Combine(Data, "agents.json"), true);
            string custom = Path.Combine(Path.Combine(Path.Combine(Root, "assets"), "sprites"), "custom");
            if (Directory.Exists(custom))
                foreach (var file in Directory.GetFiles(custom, "*", SearchOption.AllDirectories))
                {
                    string dest = CustomSprite.Folder + file.Substring(custom.Length);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(file, dest, true);
                }
        }
    }

    // Tray icon (user, 2026-11-01: the scaled-down app icon was a dark blur on the taskbar): the app's red
    // ring drawn crisply at the tray's own size on a clear background, with a white glint, plus a status dot
    // bottom right (green working, amber needs you, red failed) inside a dark cut-out. Built once per state.
    sealed class TrayIcons : IDisposable
    {
        readonly Dictionary<int, Icon> cache = new Dictionary<int, Icon>();
        readonly List<IntPtr> handles = new List<IntPtr>();
        readonly Size size;

        public TrayIcons(Size size) { this.size = size; }

        // kind: -1 none, else the dot's state (Working, NeedsYou, Error).
        public Icon For(int kind)
        {
            Icon icon;
            if (cache.TryGetValue(kind, out icon)) return icon;
            int s = Math.Max(16, size.Width);
            using (var bmp = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TranslateTransform(s / 2f, s / 2f);
                    g.RotateTransform(-40);
                    float stroke = s * 0.16f;
                    var ring = new RectangleF(-s * 0.27f, -s * 0.42f, s * 0.54f, s * 0.84f);
                    using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(RectangleF.Inflate(ring, stroke, stroke), Theme.Hex(0xFF6B80), Theme.Hex(0xB8001F), 90f))
                    using (var pen = new Pen(b, stroke)) g.DrawEllipse(pen, ring);
                    using (var glint = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, s * 0.05f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                        g.DrawArc(glint, RectangleF.Inflate(ring, stroke * 0.22f, stroke * 0.22f), 200, 55);   // on the outer half of the ring
                    g.ResetTransform();
                    if (kind >= 0)
                    {
                        float d = s * 0.42f, x = s - d, y = s - d;
                        using (var cut = new SolidBrush(Theme.PetShadow)) g.FillEllipse(cut, x, y, d, d);   // on the taskbar, not our window: dark in both themes
                        using (var core = new SolidBrush(StatusDot.ColorOf((DotKind)kind))) g.FillEllipse(core, x + d * 0.2f, y + d * 0.2f, d * 0.6f, d * 0.6f);
                    }
                }
                IntPtr h = bmp.GetHicon();
                handles.Add(h);
                icon = Icon.FromHandle(h);
            }
            cache[kind] = icon;
            return icon;
        }

        public void Dispose()
        {
            foreach (var h in handles) DestroyIcon(h);
            handles.Clear();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    }

    // Tray icon + menu; owns the event server and the pets.
    sealed class TrayApp : ApplicationContext
    {
        readonly Settings settings;
        readonly EventServer server = new EventServer();
        readonly PetManager pets;
        readonly NotifyIcon tray;
        readonly ClaudeSessions claudeSessions = new ClaudeSessions();
        readonly CodexWatcher codex = new CodexWatcher();
        readonly AppWatcher apps;
        ShellHook shellHook;

        public TrayApp(Settings settings, bool demo, int openSettings, bool openMenu)
        {
            this.settings = settings;
            Theme.ReduceMotion = settings.ReduceMotion;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) => Theme.ReduceMotion = settings.ReduceMotion;   // Windows switch flipped
            var library = new SpriteLibrary(Path.Combine(Path.Combine(Program.Root, "assets"), "sprites")) { Custom = settings.CustomSprites };
            // The user's agents.json, or the shipped defaults until they change something
            var agents = Settings.LoadAgents(File.Exists(AgentsPath) ? AgentsPath : Path.Combine(Path.Combine(Program.Root, "assets"), "factory-agents.json"));

            string error;
            bool listening = server.Start(settings.Port, out error);
            pets = new PetManager(settings, agents, AgentsPath, library, server);

            // Activity sources: each runs on its own background thread and feeds Sources.Queue.
            apps = new AppWatcher(Path.Combine(Path.Combine(Program.Root, "assets"), "detect.json"));
            claudeSessions.Start();
            UsageHistory.Start();
            pets.OpenSettings = () => ShowSettings();
            pets.OpenSettingsPage = page => ShowSettings(Math.Max(0, Math.Min(SettingsForm.PageCount - 1, page)));
            pets.SwitchTheme = dark => { if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.SwitchTheme(dark); };
            codex.Start();
            apps.Start();
            shellHook = new ShellHook();   // taskbar flashes: replies that finish while an app is hidden

            trayIcons = new TrayIcons(SystemInformation.SmallIconSize);
            tray = new NotifyIcon
            {
                Icon = trayIcons.For(-1),
                Text = "OpaGachi " + Application.ProductVersion,
                ContextMenuStrip = BuildMenu(),
                Visible = true,
            };
            tray.DoubleClick += (s, e) => ShowSettings();
            trayTimer.Tick += (s, e) => UpdateTrayDot();
            trayTimer.Start();
            Log.Write("started " + Application.ProductVersion + "; root=" + Program.Root + " data=" + Program.Data + " sprite=" + library.Width + "x" + library.Height
                      + (listening ? " listening on 127.0.0.1:" + settings.Port : " NOT listening: " + error));
            if (!listening)
                tray.ShowBalloonTip(8000, "OpaGachi", "Port " + settings.Port + " is busy, so agents can't reach the pets. Change \"port\" in %LOCALAPPDATA%\\OpaGachi\\settings.json.", ToolTipIcon.Warning);
            else if (demo)
                pets.StartDemo();

            if (openMenu)   // dev aid: show the tray menu without clicking, for screenshots
            {
                var show = new System.Windows.Forms.Timer { Interval = 600 };
                show.Tick += (s, e) => { show.Stop(); show.Dispose(); tray.ContextMenuStrip.Show(Screen.PrimaryScreen.WorkingArea.Right - 420, Screen.PrimaryScreen.WorkingArea.Bottom - 300); };
                show.Start();
            }
            if (openSettings >= 0)
            {
                var open = new System.Windows.Forms.Timer { Interval = 600 };
                open.Tick += (s, e) => { open.Stop(); open.Dispose(); ShowSettings(); settingsForm.ShowPage(Math.Min(SettingsForm.PageCount - 1, openSettings)); };
                open.Start();
            }
            else if ((settings.ShowWelcome && !demo && !Program.Args.Contains("--background")) || Program.Args.Contains("--welcome"))
            {
                var once = new System.Windows.Forms.Timer { Interval = 600 };   // after the message loop is running
                once.Tick += (s, e) => { once.Stop(); once.Dispose(); ShowWelcome(); };
                once.Start();
            }
            // Welcome offers the hooks itself; a quiet (tray-only) start never pops up.
            if (settings.HookReminder && !demo && !Program.Args.Contains("--background") && !Program.Args.Contains("--welcome")
                && !(settings.ShowWelcome && openSettings < 0))
            {
                var remind = new System.Windows.Forms.Timer { Interval = 1500 };   // after Settings, if that opens
                remind.Tick += (s, e) => { remind.Stop(); remind.Dispose(); RemindHooks(); };
                remind.Start();
            }
            if (Program.Args.Contains("--placement"))   // dev aid: the custom location editor on its own; the result goes to the log
            {
                var once = new System.Windows.Forms.Timer { Interval = 600 };
                once.Tick += (s, e) =>
                {
                    once.Stop(); once.Dispose();
                    int step, count;
                    using (var editor = new PlacementForm(pets.TargetScreen(), pets.RowPreview(out step, out count), step, count, settings.CustomX, settings.CustomY))
                        Log.Write("placement editor: " + editor.ShowDialog() + " " + editor.X.ToString("0.000") + ", " + editor.Y.ToString("0.000"));
                };
                once.Start();
            }
        }

        string AgentsPath { get { return Path.Combine(Program.Data, "agents.json"); } }

        readonly TrayIcons trayIcons;
        readonly System.Windows.Forms.Timer trayTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        int trayDot = -1;

        // The tray dot follows the most urgent pet: needs you, then failed, then working.
        void UpdateTrayDot()
        {
            var live = pets.Live.Where(p => !p.InstanceKey.EndsWith(":preview", StringComparison.Ordinal) && p.AgentKey != "demo").ToList();
            int dot = live.Any(p => p.Mode == Mode.Help) ? (int)DotKind.NeedsYou
                : live.Any(p => p.Mode == Mode.FailedHold) ? (int)DotKind.Error
                : live.Any(p => p.IsBusy) ? (int)DotKind.Working : -1;
            if (dot == trayDot) return;
            trayDot = dot;
            tray.Icon = trayIcons.For(dot);
        }

        SettingsForm settingsForm;
        WelcomeForm welcomeForm;

        void ShowSettings() { ShowSettings(-1); }

        void ShowSettings(int page)
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                if (page >= 0) settingsForm.ShowPage(page);
                settingsForm.Activate();
                return;
            }
            settingsForm = NewSettings();
            if (page >= 0) settingsForm.ShowPage(page);
            settingsForm.Show();
        }

        SettingsForm NewSettings()
        {
            var form = new SettingsForm(settings, pets);
            // Only this window's own closing clears the field: after a theme switch the old window closes while
            // its replacement is already open (that order left the field empty, so the next switch crashed).
            form.FormClosed += (s, e) => { if (settingsForm == form) settingsForm = null; };
            // A new look: the replacement is built on the same tab and category and placed exactly over the old
            // window before it shows, painted, and only then is the old one closed, so nothing moves (user, 2026-11-02).
            form.AppearanceChanged = (tab, category) =>
            {
                var next = NewSettings();
                next.StartPosition = FormStartPosition.Manual;
                next.Location = form.Location;
                next.Restyled = true;
                next.ShowPage(tab);
                next.OpenCategory(category);
                settingsForm = next;
                next.Show();
                next.Refresh();
                form.Close();
            };
            return form;
        }

        // Installed agents without OpaGachi's hooks (user, 2026-11-02): suggest the Connections page, or later,
        // or never again.
        void RemindHooks()
        {
            var missing = Integrations.MissingHooks();
            if (missing.Count == 0) return;
            string names = missing.Count == 1 ? missing[0] : string.Join(", ", missing.Take(missing.Count - 1)) + " and " + missing.Last();
            using (var ask = new ConfirmForm("Hey, we noticed you don't have hooks installed for " + names + ". For OpaGachi to run its best, we recommend turning them on.",
                "Open Connections", "Later", "Don't show this again"))
            {
                bool open = ask.ShowDialog(settingsForm != null && !settingsForm.IsDisposed ? settingsForm : null) == DialogResult.OK;
                if (ask.Checked) { settings.HookReminder = false; settings.Save(); }
                if (open) ShowSettings(SettingsForm.ConnectionsPage);
            }
        }

        void ShowWelcome()
        {
            if (welcomeForm != null && !welcomeForm.IsDisposed) { welcomeForm.Activate(); return; }
            welcomeForm = new WelcomeForm(settings, pets);
            welcomeForm.FormClosed += (s, e) =>
            {
                var form = welcomeForm;
                welcomeForm = null;
                if (form.DontShowAgain == settings.ShowWelcome)
                {
                    settings.ShowWelcome = !form.DontShowAgain;
                    settings.Save();
                }
                if (form.HooksAdded && Program.AskRestart(null, "--background")) return;
                if (form.RunDemoRequested) pets.StartDemo();
                else if (!Theme.ReduceMotion) pets.Greet();
            };
            welcomeForm.Show();
        }

        // Tray menu (Gemini Response 2, 2.5): Settings, a live row per agent with its status dot, then the
        // utilities. Agent rows are rebuilt each time the menu opens.
        ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = true, ShowCheckMargin = false };
            menu.Font = Theme.Ui(9F);
            MenuFade.Attach(menu);

            var settingsItem = Item("Settings...", (s, e) => ShowSettings());
            settingsItem.Font = Theme.Semibold(9F);    // default action (also tray double-click)
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            var agentsEnd = new ToolStripSeparator();
            menu.Items.Add(agentsEnd);

            var position = Item("Position", null);
            foreach (Anchor a in Enum.GetValues(typeof(Anchor)))
            {
                var anchor = a;
                position.DropDownItems.Add(Item(Theme.Sentence(a.ToString()), (s, e) => pets.SetAnchor(anchor)));
            }
            position.DropDownOpening += (s, e) =>
            {
                foreach (ToolStripMenuItem item in position.DropDownItems) item.Checked = item.Text == Theme.Sentence(settings.Anchor.ToString());
            };
            menu.Items.Add(position);

            var demo = Item("Demo", null);
            demo.DropDownItems.Add(Item("Run walkthrough", (s, e) => pets.StartDemo()));
            demo.DropDownItems.Add(Item("Remove demo pet", (s, e) => pets.StopDemo()));
            demo.DropDownItems.Add(Item("Preview all pets", (s, e) => pets.PreviewAll()));
            demo.DropDownItems.Add(Item("Clear preview", (s, e) => pets.ClearPreview()));
            demo.DropDownItems.Add(new ToolStripSeparator());
            foreach (Anim a in Enum.GetValues(typeof(Anim)))
            {
                var anim = a;
                demo.DropDownItems.Add(Item("Play " + Theme.Sentence(a.ToString()).ToLowerInvariant(), (s, e) => pets.ForceAll(anim)));
            }
            menu.Items.Add(demo);
            menu.Items.Add(Item("Welcome screen...", (s, e) => ShowWelcome()));
            menu.Items.Add(Item("Open log", (s, e) => Open(Path.Combine(Program.Data, "opagachi.log"))));
            menu.Items.Add(Item("Restart", (s, e) => Program.Restart("")));   // "Later" in the restart prompt comes back here
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Exit", (s, e) => ExitThread()));

            menu.Opening += (s, e) =>
            {
                // Replace the agent rows between the two top separators with fresh ones.
                int start = 2;
                while (menu.Items[start] != agentsEnd) menu.Items.RemoveAt(start);
                foreach (var info in pets.Agents.Values.Where(a => !a.Hidden))
                    menu.Items.Insert(start++, AgentItem(info));
            };
            return menu;
        }

        ToolStripMenuItem AgentItem(AgentInfo info)
        {
            var status = Integrations.StatusOf(info.Key, info, pets);
            string text = info.Name + " — " + status.Short + (status.Via.Length > 0 ? " (" + status.Via + ")" : "");
            var item = Item(text, null);
            item.Tag = new MenuAgent { Name = info.Name, Active = status.Active, Muted = status.Dot == DotKind.NotInstalled };
            item.Image = DarkMenuRenderer.Dot(status.Dot);
            if (status.Dot == DotKind.NotInstalled)
            {
                item.Enabled = false;
                return item;
            }
            var key = info.Key;
            var pet = pets.Live.FirstOrDefault(p => p.AgentKey == key && p.HostWindow != IntPtr.Zero);
            var show = Item("Show window", (s, e) => { if (pet != null) { if (Native.IsIconic(pet.HostWindow)) Native.ShowWindow(pet.HostWindow, Native.SW_RESTORE); Native.SetForegroundWindow(pet.HostWindow); } });
            show.Enabled = pet != null;
            item.DropDownItems.Add(show);
            item.DropDownItems.Add(Item(info.Enabled ? "Turn pets off" : "Turn pets on", (s, e) => { info.Enabled = !info.Enabled; pets.SaveAgents(); pets.ApplyAll(); }));
            item.DropDownItems.Add(new ToolStripSeparator());
            item.DropDownItems.Add(Item("Ties and names...", (s, e) => ShowSettings(1)));
            item.DropDownItems.Add(Item("Connections...", (s, e) => ShowSettings(2)));
            item.DropDownItems.Add(Item("Usage...", (s, e) => ShowSettings(3)));
            return item;
        }

        static ToolStripMenuItem Item(string text, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text);
            if (onClick != null) item.Click += onClick;
            var renderer = new DarkMenuRenderer();
            item.DropDown.Renderer = renderer;           // submenus share the dark style
            MenuFade.Attach(item.DropDown);
            return item;
        }

        static void Open(string path)
        {
            try { Process.Start(path); } catch (Exception) { }
        }

        protected override void ExitThreadCore()
        {
            trayTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
            trayIcons.Dispose();
            claudeSessions.Stop();
            UsageHistory.Stop();
            codex.Stop();
            apps.Stop();
            if (shellHook != null) shellHook.Dispose();
            pets.Dispose();
            server.Stop();
            Log.Write("exit");
            base.ExitThreadCore();
        }
    }
}
