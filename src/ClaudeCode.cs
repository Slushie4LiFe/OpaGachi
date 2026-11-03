using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpaGachi
{
    // Claude Code adapter. Claude Code posts its hook events to http://127.0.0.1:<port>/claude-code
    // (HTTP hooks: in order, and non-blocking if the app isn't running). A small async command hook
    // (OpaGachi.exe --hook-hostinfo) reports which window and process belong to each session.
    static class ClaudeCode
    {
        public const string Agent = "claude";
        public const string HookPath = "/claude-code";
        public const string HostPath = "/claude-code/host";

        // Reading and searching count as Thinking ("thinking and researching are grouped together").
        static readonly HashSet<string> ThinkTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Read", "Grep", "Glob", "LS", "WebSearch", "WebFetch", "ToolSearch", "Agent", "Task",
            "TodoWrite", "TaskCreate", "TaskUpdate", "TaskList", "TaskGet", "NotebookRead",
            "ListMcpResourcesTool", "ReadMcpResourceTool", "Skill",
        };

        // Tools that stop and wait for the user.
        public static readonly HashSet<string> AskTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AskUserQuestion", "ExitPlanMode",
        };

        public static string InstanceKey(IDictionary<string, object> body)
        {
            string session = Json.Str(body, "session_id", null);
            return session == null ? null : "claude-code:" + session;
        }

        // Maps one hook event onto pet signals. `waitingOnUser` is true while the pet shows Help.
        public static List<Signal> Map(IDictionary<string, object> body, bool waitingOnUser, out bool closed)
        {
            var signals = new List<Signal>();
            closed = false;
            string ev = Json.Str(body, "hook_event_name", "");
            string tool = Json.Str(body, "tool_name", "");
            switch (ev)
            {
                case "SessionStart":
                    signals.Add(Signal.Opened);
                    break;
                case "SessionEnd":
                    closed = true;
                    break;
                case "UserPromptSubmit":
                    signals.Add(Signal.UserPrompted);
                    break;
                case "PreToolUse":
                    if (waitingOnUser) signals.Add(Signal.Approved);
                    signals.Add(AskTools.Contains(tool) ? Signal.NeedsApproval : ClassifyTool(tool));
                    break;
                case "PostToolUse":
                    if (waitingOnUser) signals.Add(Signal.Approved);
                    signals.Add(Signal.Think);          // the model reads the result before its next step
                    break;
                case "PostToolUseFailure":
                    if (Json.Bool(body, "is_interrupt")) break;
                    if (waitingOnUser && LooksLikeRejection(Json.Str(body, "error", "")))
                    {
                        signals.Add(Signal.Denied);     // the user said no to the permission prompt
                        break;
                    }
                    if (waitingOnUser) signals.Add(Signal.Approved);   // it ran, so it was approved; Fail queues after Thank
                    signals.Add(Signal.Error);
                    signals.Add(Signal.Think);
                    break;
                case "PermissionRequest":
                case "Elicitation":
                    signals.Add(Signal.NeedsApproval);
                    break;
                case "Notification":
                    string kind = Json.Str(body, "notification_type", "");
                    if (kind == "permission_prompt" || kind == "elicitation_dialog") signals.Add(Signal.NeedsApproval);
                    break;
                case "PermissionDenied":
                    signals.Add(Signal.Error);          // auto mode refused the action
                    break;
                case "Stop":
                    signals.Add(Signal.Done);
                    break;
                case "StopFailure":
                    signals.Add(Signal.FatalError);
                    break;
            }
            return signals;
        }

        public static bool LooksLikeRejection(string error)
        {
            string e = error.ToLowerInvariant();
            return e.Contains("doesn't want to proceed") || e.Contains("does not want to proceed")
                || e.Contains("rejected") || e.Contains("denied by user") || e.Contains("user denied");
        }

        public static Signal ClassifyTool(string tool)
        {
            if (tool.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0) return Signal.ImageGen;
            if (ThinkTools.Contains(tool) || tool.StartsWith("mcp__", StringComparison.Ordinal) && tool.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0)
                return Signal.Think;
            return Signal.Work;
        }

        // ---- --hook-hostinfo -----------------------------------------------------------------
        // Runs as a short-lived child of Claude Code. Finds the claude process among its ancestors,
        // borrows that process's console to find the terminal window, and reports both to the app.

        static readonly string[] AgentProcessNames = { "claude.exe", "grok.exe", "node.exe", "bun.exe" };

        public static int RunHostInfo(int port)
        {
            var body = HookFiles.ReadStdin();
            string session = Json.Str(body, "session_id", null);
            bool grok = GrokHooks.IsGrokPayload(body);   // Grok Build runs Claude Code's hooks too
            if (grok) session = Json.Str(body, "sessionId", null);
            if (session == null) return 0;

            var chain = Ancestors();
            int agentPid = 0;
            int agentIndex = -1;
            for (int i = 0; i < chain.Count; i++)
            {
                if (AgentProcessNames.Contains(chain[i].Value, StringComparer.OrdinalIgnoreCase))
                {
                    agentPid = chain[i].Key;
                    agentIndex = i;
                    break;
                }
            }

            string how = "none";
            IntPtr host = agentPid != 0 ? ConsoleHost(agentPid, out how) : IntPtr.Zero;
            for (int i = agentIndex + 1; host == IntPtr.Zero && i < chain.Count; i++)
            {
                host = VisibleWindowOf(chain[i].Key);
                if (host != IntPtr.Zero) how = "ancestor " + chain[i].Value;
            }

            var report = new Dictionary<string, object>
            {
                { "session_id", session },
                { "agent", grok ? GrokHooks.Agent : Agent },
                { "host_hwnd", host.ToInt64() },
                { "agent_pid", agentPid },
            };
            bool sent = EventServer.Post(port, HostPath, Json.Write(report), 1500);
            Log.Write(string.Format("hostinfo: session={0} agent_pid={1} host={2} ({3}) '{4}' [{5}] sent={6} chain={7}",
                session, agentPid, host.ToInt64(), how, host == IntPtr.Zero ? "" : Native.WindowText(host),
                host == IntPtr.Zero ? "" : Native.WindowClass(host), sent,
                string.Join(" < ", chain.Select(p => p.Value + ":" + p.Key))));
            return 0;
        }

        // The visible window that owns a process's console: the terminal (Windows Terminal, conhost, ...).
        public static IntPtr ConsoleHost(int pid, out string how)
        {
            how = "none";
            Native.FreeConsole();
            if (!Native.AttachConsole((uint)pid)) return IntPtr.Zero;
            IntPtr console = Native.GetConsoleWindow();
            Native.FreeConsole();
            if (console == IntPtr.Zero) return IntPtr.Zero;
            IntPtr owner = Native.GetAncestor(console, Native.GA_ROOTOWNER);
            if (owner != IntPtr.Zero && Native.IsWindowVisible(owner)) { how = "console owner"; return owner; }
            if (Native.IsWindowVisible(console)) { how = "console"; return console; }
            return IntPtr.Zero;
        }

        // --host-of <pid>: run by ClaudeSessions as a throwaway helper; the exit code is the window handle
        // (window handles fit in 32 bits, even in 64-bit Windows).
        public static int RunHostOf(int pid)
        {
            string how;
            return ConsoleHost(pid, out how).ToInt32();
        }

        public static List<KeyValuePair<int, string>> AncestorChain() { return Ancestors(); }

        // Parent chain of this process: (pid, exe name), nearest first, excluding this process.
        static List<KeyValuePair<int, string>> Ancestors()
        {
            var parent = new Dictionary<int, int>();
            var names = new Dictionary<int, string>();
            IntPtr snap = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
            try
            {
                var e = new Native.PROCESSENTRY32W { dwSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.PROCESSENTRY32W)) };
                for (bool ok = Native.Process32FirstW(snap, ref e); ok; ok = Native.Process32NextW(snap, ref e))
                {
                    parent[e.th32ProcessID] = e.th32ParentProcessID;
                    names[e.th32ProcessID] = e.szExeFile;
                }
            }
            finally { Native.CloseHandle(snap); }

            var chain = new List<KeyValuePair<int, string>>();
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var seen = new HashSet<int> { pid };
            int next;
            while (chain.Count < 16 && parent.TryGetValue(pid, out next) && names.ContainsKey(next) && seen.Add(next))
            {
                chain.Add(new KeyValuePair<int, string>(next, names[next]));
                pid = next;
            }
            return chain;
        }

        static IntPtr VisibleWindowOf(int pid)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, _) =>
            {
                uint owner;
                Native.GetWindowThreadProcessId(hwnd, out owner);
                if (owner == pid && Native.IsWindowVisible(hwnd) && Native.WindowText(hwnd).Length > 0)
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        // ---- Installing hooks into a Claude Code settings.json -------------------------------

        static readonly string[] HttpEvents =
        {
            "SessionStart", "SessionEnd", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure",
            "PermissionRequest", "PermissionDenied", "Notification", "Elicitation", "Stop", "StopFailure",
        };
        static readonly string[] HostInfoEvents = { "SessionStart", "UserPromptSubmit" };

        public static string DefaultSettingsPath()
        {
            return Path.Combine(Path.Combine(Sources.Home, ".claude"), "settings.json");
        }

        public static void InstallHooks(string settingsPath, int port, string exePath)
        {
            var root = LoadForEdit(settingsPath);
            var existing = Get(root, "hooks") as IDictionary<string, object>;
            var order = (existing == null ? new List<string>() : existing.Keys.ToList()).Concat(HttpEvents).Distinct().ToList();
            var hooks = RemoveOurs(root);
            string url = "http://127.0.0.1:" + port + HookPath;
            string command = "\"" + exePath.Replace('\\', '/') + "\" --hook-hostinfo";
            foreach (var ev in HttpEvents)
            {
                var handlers = new List<object>
                {
                    new Dictionary<string, object> { { "type", "http" }, { "url", url }, { "timeout", 2 } },
                };
                if (HostInfoEvents.Contains(ev))
                    handlers.Add(new Dictionary<string, object> { { "type", "command" }, { "command", command }, { "async", true }, { "timeout", 10 } });
                var groups = GroupList(hooks, ev);
                groups.Add(new Dictionary<string, object> { { "hooks", handlers.ToArray() } });
                hooks[ev] = groups.ToArray();
            }
            var ordered = new Dictionary<string, object>();   // keep the user's event order, ours after
            foreach (var ev in order) if (hooks.ContainsKey(ev)) ordered[ev] = hooks[ev];
            root["hooks"] = ordered;
            Save(settingsPath, root);
            FactoryReset.RegisterHookPath("claude", settingsPath);
        }

        public static void UninstallHooks(string settingsPath)
        {
            if (!File.Exists(settingsPath)) return;
            var root = LoadForEdit(settingsPath);
            var hooks = RemoveOurs(root);
            if (hooks.Count == 0) root.Remove("hooks");
            else root["hooks"] = hooks;
            Save(settingsPath, root);
        }

        public static bool HooksInstalled(string settingsPath)
        {
            var root = Json.ReadFile(settingsPath);
            var hooks = root == null ? null : Get(root, "hooks") as IDictionary<string, object>;
            if (hooks == null) return false;
            return hooks.Values.OfType<object[]>().SelectMany(g => g).OfType<IDictionary<string, object>>()
                .Select(g => Get(g, "hooks") as object[]).Where(h => h != null).SelectMany(h => h)
                .OfType<IDictionary<string, object>>().Any(IsOurs);
        }

        public static IDictionary<string, object> LoadForEdit(string path)
        {
            if (!File.Exists(path)) return new Dictionary<string, object>();
            var root = Json.Parse(File.ReadAllText(path));
            if (root == null) throw new InvalidDataException(path + " is not a JSON object; not touching it.");
            return root;
        }

        public static void Save(string path, IDictionary<string, object> root)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path)) File.Copy(path, path + ".opagachi-backup", true);
            Json.WriteFile(path, root);
        }

        // Removes our handlers (and any groups/events left empty). Returns the remaining hooks object.
        static IDictionary<string, object> RemoveOurs(IDictionary<string, object> root)
        {
            var hooks = Get(root, "hooks") as IDictionary<string, object> ?? new Dictionary<string, object>();
            foreach (var ev in hooks.Keys.ToList())
            {
                var groups = hooks[ev] as object[];
                if (groups == null) continue;
                var kept = new List<object>();
                foreach (var g in groups)
                {
                    var group = g as IDictionary<string, object>;
                    var handlers = group == null ? null : Get(group, "hooks") as object[];
                    if (handlers == null) { kept.Add(g); continue; }
                    var rest = handlers.Where(h => !(h is IDictionary<string, object> && IsOurs((IDictionary<string, object>)h))).ToArray();
                    if (rest.Length == 0) continue;
                    group["hooks"] = rest;
                    kept.Add(group);
                }
                if (kept.Count == 0) hooks.Remove(ev);
                else hooks[ev] = kept.ToArray();
            }
            return hooks;
        }

        static bool IsOurs(IDictionary<string, object> handler)
        {
            string url = Json.Str(handler, "url", "");
            string command = Json.Str(handler, "command", "");
            return (url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) && url.EndsWith(HookPath, StringComparison.Ordinal))
                || command.Contains("--hook-hostinfo");
        }

        public static List<object> GroupList(IDictionary<string, object> hooks, string ev)
        {
            var existing = Get(hooks, ev) as object[];
            return existing == null ? new List<object>() : existing.ToList();
        }

        public static object Get(IDictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }
    }
}
