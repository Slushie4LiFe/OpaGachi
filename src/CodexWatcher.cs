using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OpaGachi
{
    // ChatGPT desktop app (built on Codex) and the Codex CLI. Both append every step of a thread to
    // ~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl: task started, reasoning, tool calls, task complete.
    // Following those files gives exact states with nothing installed. Threads from the desktop app
    // (including its sub-agents) drive the ChatGPT app's pet; CLI and editor threads drive a "Codex" pet.
    // Approval requests aren't written to these files; the optional Codex hook covers those.
    sealed class CodexWatcher
    {
        public const string Origin = "codex";
        public static volatile int BusyThreads;
        public static volatile bool DesktopTracked;
        bool seeding;
        public const string AppInstance = "app:chatgpt";     // same pet as the ChatGPT app window
        public const string CliInstance = "chatgpt:codex";
        const string Agent = "chatgpt";

        readonly string root, sessions;
        readonly Thread thread;
        volatile bool stopped;
        FileSystemWatcher watcher;
        readonly ConcurrentQueue<KeyValuePair<string, bool>> hints = new ConcurrentQueue<KeyValuePair<string, bool>>();   // path, created
        readonly Dictionary<string, Rollout> tracked = new Dictionary<string, Rollout>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> loggedTypes = new HashSet<string>();
        bool cliPresent;
        DateTime cliLastActive;

        static readonly Regex OriginatorRx = new Regex("\"originator\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);
        static readonly HashSet<string> ThinkTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "update_plan", "view_image", "web_search", "read_file", "list_dir", "grep_files", "search", "list_mcp_resources", "read_mcp_resource",
        };

        sealed class Rollout
        {
            public string Path, Instance;
            public JsonlTail Tail;
            public bool Busy;
        }

        public CodexWatcher()
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME");
            root = string.IsNullOrEmpty(home) ? Path.Combine(Sources.Home, ".codex") : home;
            sessions = Path.Combine(root, "sessions");
            thread = new Thread(Run) { IsBackground = true, Name = "codex", Priority = ThreadPriority.BelowNormal };
        }

        public static bool Installed { get { return Directory.Exists(Path.Combine(Path.Combine(Sources.Home, ".codex"), "sessions")); } }

        public void Start() { thread.Start(); }
        public void Stop() { stopped = true; }

        void Run()
        {
            DateTime nextScan = DateTime.MinValue;
            try { CodexUsage.Seed(sessions); }
            catch (Exception e) { Log.Write("codex usage seed: " + e.Message); }
            while (!stopped)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    if (watcher == null && Directory.Exists(sessions)) StartWatcher();
                    KeyValuePair<string, bool> hint;
                    while (hints.TryDequeue(out hint))
                        if (!tracked.ContainsKey(hint.Key)) Track(hint.Key, !hint.Value);
                    if (DateTime.UtcNow >= nextScan)
                    {
                        nextScan = DateTime.UtcNow.AddSeconds(10);
                        ScanRecent();   // backup for missed watcher events
                    }
                    foreach (var r in tracked.Values.ToList()) Follow(r);
                    UpdateCliPresence();
                    BusyThreads = tracked.Values.Count(t => t.Busy);
                    DesktopTracked = tracked.Values.Any(t => t.Instance == AppInstance);
                    Sources.Status["codex"] = !Directory.Exists(sessions) ? "Codex not found"
                        : tracked.Values.Count(r => r.Busy) > 0 ? "Working (" + tracked.Values.Count(r => r.Busy) + " thread" + (tracked.Values.Count(r => r.Busy) == 1 ? ")" : "s)")
                        : "Watching";
                }
                catch (Exception e) { Log.Write("codex watcher: " + e.Message); }
                Interlocked.Add(ref Sources.CodexTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
                Thread.Sleep(400);
            }
            if (watcher != null) watcher.Dispose();
            foreach (var r in tracked.Values) r.Tail.Dispose();
        }

        void StartWatcher()
        {
            watcher = new FileSystemWatcher(sessions, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += (s, e) => hints.Enqueue(new KeyValuePair<string, bool>(e.FullPath, true));
            watcher.Changed += (s, e) => hints.Enqueue(new KeyValuePair<string, bool>(e.FullPath, false));
            watcher.EnableRaisingEvents = true;
            Log.Write("codex: watching " + sessions);
        }

        // Today's and yesterday's folders: pick up anything written in the last minute that we aren't following.
        void ScanRecent()
        {
            if (!Directory.Exists(sessions)) return;
            foreach (var day in new[] { DateTime.Now, DateTime.Now.AddDays(-1) })
            {
                string dir = Path.Combine(sessions, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "rollout-*.jsonl"))
                {
                    if (tracked.ContainsKey(file)) continue;
                    try
                    {
                        if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalSeconds < 60) Track(file, true);
                    }
                    catch (Exception) { }
                }
            }
        }

        void Track(string path, bool fromEnd)
        {
            if (!File.Exists(path)) return;
            string originator = ReadOriginator(path);
            if (originator == "__guardian__") return;
            bool desktop = originator.IndexOf("desktop", StringComparison.OrdinalIgnoreCase) >= 0;
            try
            {
                var r = new Rollout { Path = path, Instance = desktop ? AppInstance : CliInstance, Tail = new JsonlTail(path, false) };
                tracked[path] = r;
                if (fromEnd)
                {
                    seeding = true;
                    try { foreach (var line in r.Tail.ReadNew()) Handle(r,line); }
                    finally { seeding = false; }
                    if (r.Busy) Send(r.Instance, Signal.Think);
                }
                Log.Write("codex: following " + Path.GetFileName(path) + " (" + (originator.Length == 0 ? "unknown origin" : originator) + (fromEnd ? ", from now" : ", new") + ")");
            }
            catch (Exception e) { Log.Write("codex: can't open " + path + ": " + e.Message); }
        }

        // The first line is the thread's session_meta; its "originator" tells the desktop app from the CLI.
        static string ReadOriginator(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var buffer = new byte[256 * 1024];
                    int n = fs.Read(buffer, 0, buffer.Length);
                    string text = Encoding.UTF8.GetString(buffer, 0, n);
                    if (Regex.IsMatch(text, "\"thread_source\"\\s*:\\s*\"guardian_review\"")) return "__guardian__";
                    var m = OriginatorRx.Match(text);
                    return m.Success ? m.Groups[1].Value : "";
                }
            }
            catch (Exception) { return ""; }
        }

        void Follow(Rollout r)
        {
            foreach (var line in r.Tail.ReadNew()) Handle(r, line);
            double quiet = (DateTime.UtcNow - r.Tail.LastGrowth).TotalMinutes;
            if (r.Busy && quiet > 20)
            {
                r.Busy = false;   // writer died mid-turn
                if (!AnyBusy(r.Instance)) Send(r.Instance, Signal.Cancelled);
            }
            if (!r.Busy && quiet > 10)
            {
                r.Tail.Dispose();
                tracked.Remove(r.Path);
            }
        }

        void Handle(Rollout r, string line)
        {
            if (line.Length > 2 * 1024 * 1024) return;   // big tool output / images: nothing to learn
            IDictionary<string, object> o;
            try { o = Json.Parse(line); }
            catch (Exception) { return; }
            if (o == null) return;
            string type = Json.Str(o, "type", "");
            object p;
            var payload = o.TryGetValue("payload", out p) ? p as IDictionary<string, object> : null;
            string kind = Json.Str(payload, "type", "");

            if (type == "event_msg")
            {
                switch (kind)
                {
                    case "task_started":
                        Begin(r, true);
                        break;
                    case "task_completed":
                    case "task_complete":
                        End(r, Signal.Done);
                        break;
                    case "turn_aborted":
                        End(r, Signal.Cancelled);
                        break;
                    case "error":
                        End(r, Signal.FatalError);
                        break;
                    case "exec_approval_request":
                    case "apply_patch_approval_request":
                    case "request_user_input":
                    case "elicitation_request":
                        Begin(r, false);
                        Send(r.Instance, Signal.NeedsApproval);
                        break;
                    case "exec_command_begin":
                    case "patch_apply_begin":
                    case "mcp_tool_call_begin":
                        Begin(r, false);
                        Send(r.Instance, Signal.Work);
                        break;
                    case "web_search_begin":
                        Begin(r, false);
                        Send(r.Instance, Signal.Think);
                        break;
                    case "image_generation_begin":
                        Begin(r, false);
                        Send(r.Instance, Signal.ImageGen);
                        break;
                    case "token_count":
                        CodexUsage.Record(r.Path, payload);
                        break;
                    case "item_completed":
                    case "thread_settings_applied":
                    case "agent_message":
                    case "agent_reasoning":
                    case "user_message":
                        break;
                    default:
                        if (loggedTypes.Add("event_msg/" + kind)) Log.Write("codex: event '" + kind + "'");
                        break;
                }
            }
            else if (type == "response_item")
            {
                switch (kind)
                {
                    case "reasoning":
                        Begin(r, false);
                        Send(r.Instance, Signal.Think);
                        break;
                    case "function_call":
                    case "custom_tool_call":
                    case "local_shell_call":
                        Begin(r, false);
                        Send(r.Instance, ClassifyTool(Json.Str(payload, "name", kind)));
                        break;
                    case "web_search_call":
                        Begin(r, false);
                        Send(r.Instance, Signal.Think);
                        break;
                    case "image_generation_call":
                        Begin(r, false);
                        Send(r.Instance, Signal.ImageGen);
                        break;
                }
            }
        }

        static Signal ClassifyTool(string name)
        {
            if (name.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0 && name != "view_image") return Signal.ImageGen;
            return ThinkTools.Contains(name) ? Signal.Think : Signal.Work;
        }

        // A thread is busy from task_started to task_complete. If we started following mid-turn, the first
        // activity counts as the start.
        void Begin(Rollout r, bool explicitStart)
        {
            if (r.Busy) return;
            bool first = !AnyBusy(r.Instance);
            r.Busy = true;
            if (r.Instance == CliInstance) cliLastActive = DateTime.UtcNow;
            if (first) Send(r.Instance, explicitStart ? Signal.UserPrompted : Signal.Think);
        }

        // The pet only finishes when the last busy thread of its app does (sub-agents run alongside).
        void End(Rollout r, Signal how)
        {
            bool was = r.Busy;
            r.Busy = false;
            if (r.Instance == CliInstance) cliLastActive = DateTime.UtcNow;
            if (how == Signal.FatalError) { Send(r.Instance, AnyBusy(r.Instance) ? Signal.Error : Signal.FatalError); return; }
            if (was && !AnyBusy(r.Instance)) Send(r.Instance, how);
        }

        bool AnyBusy(string instance)
        {
            foreach (var t in tracked.Values) if (t.Busy && t.Instance == instance) return true;
            return false;
        }

        // The CLI has no window of its own to open or close, so its pet stays while threads are active
        // and leaves a few minutes after the last one finishes.
        void UpdateCliPresence()
        {
            bool busy = AnyBusy(CliInstance);
            bool want = busy || (cliPresent && (DateTime.UtcNow - cliLastActive).TotalMinutes < 5);
            if (want == cliPresent) return;
            cliPresent = want;
            Sources.Presence(Origin, Agent, CliInstance, want, IntPtr.Zero, 0, "Codex", false);
        }

        void Send(string instance, Signal signal)
        {
            if (seeding) return;
            if (instance == CliInstance && !cliPresent)
            {
                cliPresent = true;
                Sources.Presence(Origin, Agent, CliInstance, true, IntPtr.Zero, 0, "Codex", false);
            }
            Sources.Send(Origin, Agent, instance, signal);
        }
    }
}
