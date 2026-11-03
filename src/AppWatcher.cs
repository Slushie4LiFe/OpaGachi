using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

namespace OpaGachi
{
    // Desktop AI apps (assets/detect.json). Once a second:
    //  * Presence: the app's main window opening or closing brings its pet in or sends it away.
    //  * Activity (chat apps): reads the app's prompt box through Windows UI Automation, the accessibility
    //    interface screen readers use, so it works while the window is covered or minimised. While an app is
    //    answering, a stop button sits in the prompt box; when it goes back to send, the answer is done.
    // Only the handful of buttons around the prompt box are read each second (well under 1 ms); the whole
    // window is searched only to find the prompt box, and once at the end of an answer to spot errors.
    //
    // Hidden windows: Chromium freezes a window's accessibility tree while it's minimised or fully covered
    // (tested 2026-10-31: the page keeps running, the tree stops updating until the window shows again,
    // then catches up at once). CPU and I/O can't stand in: a covered app streaming an answer measured the
    // same as idle. So a hidden answer ends when (1) the window shows again and the tree catches up, or
    // (2) the app flashes its taskbar button (what desktop apps do when a reply lands in the background),
    // which Windows reports to us through the shell hook. After (2) the frozen "stop" button is ignored
    // until the tree changes again.
    sealed class AppWatcher
    {
        public const string Origin = "app";

        public sealed class Profile
        {
            public string Id, Agent, Label;
            public string[] Processes = new string[0];
            public bool Uia;
            public Regex Busy;
            public string[] Help = new string[0], Error = new string[0];
            public double WorkAfter = 8;   // seconds of answering before the pet switches from thinking to writing
            public string Instance { get { return "app:" + Id; } }
        }

        sealed class Watch
        {
            public Profile P;
            public IntPtr Window;
            public uint Pid;
            public AutomationElement Root, Composer;
            public DateTime NextFind, NextHelpScan, BusySince;
            public bool Busy, WorkSent, Help;
            public string LastButtons = "";
            public int Errors;
            public string FrozenButtons;   // set when a taskbar flash ended the answer; ignore the tree until it changes
        }

        // Taskbar flashes reported by the shell hook (UI thread) for the watcher thread.
        static readonly System.Collections.Concurrent.ConcurrentQueue<IntPtr> flashes = new System.Collections.Concurrent.ConcurrentQueue<IntPtr>();
        public static void Flashed(IntPtr hwnd) { flashes.Enqueue(hwnd); }

        public static List<Profile> Profiles = new List<Profile>();

        // Live state per app profile for the UI (written by the watcher thread).
        public sealed class AppState { public bool Open, Busy, Help; public IntPtr Window; public string Exe = ""; }
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, AppState> States =
            new System.Collections.Concurrent.ConcurrentDictionary<string, AppState>();
        readonly List<Watch> watches = new List<Watch>();
        readonly Thread thread;
        volatile bool stopped;
        static readonly CacheRequest ButtonCache = MakeCache();
        static readonly Condition IsButton = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
        static readonly Condition IsEdit = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);

        public AppWatcher(string detectPath)
        {
            Profiles = Load(detectPath);
            foreach (var p in Profiles) watches.Add(new Watch { P = p });
            thread = new Thread(Run) { IsBackground = true, Name = "apps", Priority = ThreadPriority.BelowNormal };
            thread.SetApartmentState(ApartmentState.MTA);
        }

        public void Start() { thread.Start(); }
        public void Stop() { stopped = true; }

        // Exe names of an agent's desktop apps (for "hide in front of its own app").
        public static IEnumerable<string> ProcessesOf(string agent)
        {
            foreach (var p in Profiles)
                if (p.Agent == agent)
                    foreach (var exe in p.Processes) yield return exe;
        }

        static CacheRequest MakeCache()
        {
            var c = new CacheRequest { AutomationElementMode = AutomationElementMode.None };
            c.Add(AutomationElement.IsEnabledProperty); c.Add(AutomationElement.IsOffscreenProperty);
            c.Add(AutomationElement.NameProperty);
            c.Add(AutomationElement.AutomationIdProperty);
            c.Add(AutomationElement.ClassNameProperty);
            return c;
        }

        static List<Profile> Load(string path)
        {
            var list = new List<Profile>();
            var json = Json.ReadFile(path);
            if (json == null) return list;
            string busy = Json.Str(json, "busy", "^(stop|cancel)\\b");
            object apps;
            if (!json.TryGetValue("apps", out apps) || !(apps is object[])) return list;
            foreach (var item in (object[])apps)
            {
                var d = item as IDictionary<string, object>;
                if (d == null) continue;
                var p = new Profile
                {
                    Id = Json.Str(d, "id", null),
                    Agent = Json.Str(d, "agent", null),
                    Label = Json.Str(d, "label", ""),
                    Processes = Json.Strings(d, "processes"),
                    Uia = Json.BoolOr(d, "uia", true),
                    Help = Json.Strings(d, "help"),
                    Error = Json.Strings(d, "error"),
                    WorkAfter = Json.Num(d, "workAfter", 8),
                };
                if (p.Id == null || p.Agent == null || p.Processes.Length == 0) continue;
                try { p.Busy = new Regex(Json.Str(d, "busy", busy), RegexOptions.IgnoreCase); }
                catch (ArgumentException) { p.Busy = new Regex("^(stop|cancel)\\b", RegexOptions.IgnoreCase); }
                list.Add(p);
            }
            return list;
        }

        void Run()
        {
            while (!stopped)
            {
                var started = DateTime.UtcNow;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    var windows = TopWindows();
                    IntPtr flashed;
                    while (flashes.TryDequeue(out flashed))
                        foreach (var w in watches)
                        {
                            if (w.Window != flashed) continue;
                            if (w.Busy) FinishFromFlash(w);
                            else Log.Write("app: " + w.P.Label + " flashed its taskbar button");   // learning which apps do this
                        }
                    foreach (var w in watches)
                    {
                        Track(w, windows);
                        if (w.Window != IntPtr.Zero && w.P.Uia) Poll(w);
                    }
                    Sources.Status["apps"] = string.Join(", ", watches.Where(w => w.Window != IntPtr.Zero).Select(w => w.P.Label + (w.Busy ? " (answering)" : "")));
                    foreach (var w in watches)
                        States[w.P.Id] = new AppState { Open = w.Window != IntPtr.Zero, Busy = w.Busy, Help = w.Help, Window = w.Window };
                }
                catch (Exception e) { Log.Write("app watcher: " + e.Message); }
                Interlocked.Add(ref Sources.AppTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
                int spent = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                Thread.Sleep(Math.Max(200, 1000 - spent));
            }
        }

        sealed class TopWindow { public IntPtr Hwnd; public uint Pid; public string Exe; public long Area; }

        // Visible, unowned top-level windows with a title: the ones that show on the taskbar.
        static List<TopWindow> TopWindows()
        {
            var list = new List<TopWindow>();
            Native.EnumWindows((hwnd, _) =>
            {
                if (!Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return true;
                if ((Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) != 0) return true;
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                Native.RECT r;
                Native.GetWindowRect(hwnd, out r);
                list.Add(new TopWindow { Hwnd = hwnd, Pid = pid, Area = (long)(r.Right - r.Left) * (r.Bottom - r.Top) });
                return true;
            }, IntPtr.Zero);
            foreach (var w in list) w.Exe = Proc.ImageName(w.Pid);
            return list;
        }

        void Track(Watch w, List<TopWindow> windows)
        {
            TopWindow best = null;
            foreach (var t in windows)
            {
                if (!w.P.Processes.Any(p => string.Equals(p, t.Exe, StringComparison.OrdinalIgnoreCase))) continue;
                string title = Native.WindowText(t.Hwnd);
                if (title.Length == 0) continue;
                if (best == null || t.Area > best.Area) best = t;
            }
            IntPtr hwnd = best == null ? IntPtr.Zero : best.Hwnd;
            if (hwnd == w.Window) return;
            bool wasOpen = w.Window != IntPtr.Zero;
            w.Window = hwnd;
            w.Pid = best == null ? 0 : best.Pid;
            w.Root = w.Composer = null;
            w.NextFind = DateTime.MinValue;
            if (hwnd != IntPtr.Zero)
            {
                Sources.Presence(Origin, w.P.Agent, w.P.Instance, true, hwnd, (int)w.Pid, w.P.Label, true);
                if (!wasOpen) Log.Write("app: " + w.P.Label + " window open (" + best.Exe + ")");
            }
            else
            {
                if (w.Busy) Sources.Send(Origin, w.P.Agent, w.P.Instance, Signal.Cancelled);
                w.Busy = w.Help = false;
                Sources.Presence(Origin, w.P.Agent, w.P.Instance, false, IntPtr.Zero, 0, w.P.Label, true);
                Log.Write("app: " + w.P.Label + " window closed");
            }
        }

        void Poll(Watch w)
        {
            try
            {
                if (w.P.Instance == CodexWatcher.AppInstance && CodexWatcher.DesktopTracked) return;
                if (w.Composer == null)
                {
                    if (DateTime.UtcNow < w.NextFind) return;
                    w.NextFind = DateTime.UtcNow.AddSeconds(w.Errors > 5 ? 15 : 3);
                    FindComposer(w);
                    if (w.Composer == null) return;
                }
                var buttons = Buttons(w.Composer);
                if (buttons.Count == 0) { w.Composer = null; return; }   // prompt box re-rendered: find it again
                w.Errors = 0;
                string signature = string.Join(" | ", buttons.Select(b => b.Key));
                if (signature != w.LastButtons)
                {
                    Log.Write("app: " + w.P.Label + " prompt box buttons: " + signature);
                    w.LastButtons = signature;
                }
                bool busy = buttons.Any(b => w.P.Busy.IsMatch(b.Key));
                var now = DateTime.UtcNow;
                if (w.FrozenButtons != null)
                {
                    if (signature == w.FrozenButtons) return;   // tree still frozen on the old "stop" state
                    w.FrozenButtons = null;                      // it moved: trust it again
                    if (busy) Log.Write("app: " + w.P.Label + " still answering after all");
                }
                if (busy && !w.Busy)
                {
                    w.Busy = true;
                    w.BusySince = now;
                    w.WorkSent = false;
                    w.NextHelpScan = now.AddSeconds(2);
                    Send(w, Signal.UserPrompted);
                }
                else if (busy && !w.WorkSent && (now - w.BusySince).TotalSeconds >= w.P.WorkAfter)
                {
                    w.WorkSent = true;
                    Send(w, Signal.Work);
                }
                else if (!busy && w.Busy)
                {
                    w.Busy = false;
                    bool failed = w.P.Error.Length > 0 && Find(w, w.P.Error);
                    if (w.Help) { w.Help = false; Send(w, Signal.Approved); }
                    Send(w, failed ? Signal.FatalError : Signal.Done);
                }
                if (w.Busy && w.P.Help.Length > 0 && now >= w.NextHelpScan)
                {
                    w.NextHelpScan = now.AddSeconds(4);
                    bool help = Find(w, w.P.Help);
                    if (help && !w.Help) Send(w, Signal.NeedsApproval);
                    else if (!help && w.Help) Send(w, Signal.Approved);
                    w.Help = help;
                }
            }
            catch (Exception e)
            {
                // ElementNotAvailable (the page re-rendered), COM timeouts (the app is busy or hung), ...
                w.Composer = null;
                if (++w.Errors <= 3) Log.Write("app: " + w.P.Label + " UI Automation: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // The app flashed its taskbar button while answering: the reply landed while it was out of sight.
        void FinishFromFlash(Watch w)
        {
            Log.Write("app: " + w.P.Label + " flashed its taskbar button while answering: reply finished");
            w.Busy = false;
            w.FrozenButtons = w.LastButtons;
            if (w.Help) { w.Help = false; Send(w, Signal.Approved); }
            Send(w, Signal.Done);
        }

        // The prompt box: the last text input in the window, widened to the largest surrounding container
        // that still holds only a few buttons (send/stop, attach, voice, model picker), not the chat history.
        void FindComposer(Watch w)
        {
            if (w.Root == null) w.Root = AutomationElement.FromHandle(w.Window);
            var edits = w.Root.FindAll(TreeScope.Descendants, IsEdit);
            if (edits.Count == 0)
            {
                if (w.Errors++ < 3) Log.Write("app: " + w.P.Label + " has no text input yet (accessibility may still be starting)");
                return;
            }
            var walker = TreeWalker.ControlViewWalker;
            AutomationElement best = null, cur = edits[edits.Count - 1];
            for (int level = 0; level < 8; level++)
            {
                cur = walker.GetParent(cur);
                if (cur == null) break;
                int count = Buttons(cur).Count;
                if (count > 12) break;
                if (count >= 1) best = cur;
            }
            w.Composer = best;
            if (best != null) Log.Write("app: " + w.P.Label + " prompt box found");
        }

        // (name, automation id + class) of every button inside `scope`, fetched in one cross-process call.
        static List<KeyValuePair<string, string>> Buttons(AutomationElement scope)
        {
            var result = new List<KeyValuePair<string, string>>();
            AutomationElementCollection found;
            using (ButtonCache.Activate()) found = scope.FindAll(TreeScope.Descendants, IsButton);
            foreach (AutomationElement b in found)
            {
                var c = b.Cached;
                if (!c.IsEnabled || c.IsOffscreen) continue;
                result.Add(new KeyValuePair<string, string>(c.Name ?? "", (c.AutomationId ?? "") + " " + (c.ClassName ?? "")));
            }
            return result;
        }

        // Is there a button with one of these names anywhere in the window? (~50-100 ms on a long chat.)
        static bool Find(Watch w, string[] names)
        {
            if (w.Root == null) return false;
            var any = new OrCondition(names.Select(n => (Condition)new PropertyCondition(AutomationElement.NameProperty, n, PropertyConditionFlags.IgnoreCase))
                                           .Concat(new Condition[] { Condition.FalseCondition }).ToArray());
            return w.Root.FindFirst(TreeScope.Descendants, new AndCondition(IsButton, any)) != null;
        }

        static void Send(Watch w, Signal signal)
        {
            Log.Write("app: " + w.P.Label + " -> " + signal);
            Sources.Send(Origin, w.P.Agent, w.P.Instance, signal);
        }
    }
}

namespace OpaGachi
{
    // Receives shell notifications on the UI thread; forwards taskbar-button flashes (HSHELL_FLASH) to the
    // app watcher. Event-driven: costs nothing until a window flashes.
    sealed class ShellHook : System.Windows.Forms.NativeWindow, IDisposable
    {
        const int HSHELL_FLASH = 0x8006;
        readonly int message;

        public ShellHook()
        {
            CreateHandle(new System.Windows.Forms.CreateParams());
            message = Native.RegisterWindowMessage("SHELLHOOK");
            if (!Native.RegisterShellHookWindow(Handle)) Log.Write("shell hook: couldn't register");
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == message && m.WParam.ToInt32() == HSHELL_FLASH) AppWatcher.Flashed(m.LParam);
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Native.DeregisterShellHookWindow(Handle);
            DestroyHandle();
        }
    }
}
