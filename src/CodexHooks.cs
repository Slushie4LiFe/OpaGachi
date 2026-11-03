using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpaGachi
{
    // Optional Codex hook for the one state the thread logs don't record: waiting for your approval.
    // A PermissionRequest command hook (~/.codex/hooks.json) runs `OpaGachi.exe --codex-hook`, which tells
    // the app which pet should raise its hand. It prints nothing, so Codex shows its normal approval prompt
    // ("if no matching hook decides, Codex uses the normal approval flow"). Codex runs new hooks only after
    // you trust them with /hooks.
    static class CodexHooks
    {
        public const string HookPath = "/codex";
        const string Name = "opagachi";
        const string Flag = "--codex-hook";

        public static string DefaultPath()
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME");
            return Path.Combine(string.IsNullOrEmpty(home) ? Path.Combine(Sources.Home, ".codex") : home, "hooks.json");
        }

        public static bool Installed(string path)
        {
            var root = Json.ReadFile(path);
            var hooks = root == null ? null : ClaudeCode.Get(root, "hooks") as IDictionary<string, object>;
            if (hooks == null) return false;
            return hooks.Values.OfType<object[]>().SelectMany(g => g).OfType<IDictionary<string, object>>().Any(IsOurs);
        }

        public static void Install(string path, string exePath)
        {
            var root = ClaudeCode.LoadForEdit(path);
            var hooks = Remove(root);
            var groups = ClaudeCode.GroupList(hooks, "PermissionRequest");
            groups.Add(new Dictionary<string, object>
            {
                { "name", Name },
                { "matcher", "*" },
                { "hooks", new object[] { new Dictionary<string, object> {
                    { "type", "command" }, { "command", "\"" + exePath + "\" " + Flag + " PermissionRequest" }, { "timeout", 10 } } } },
            });
            hooks["PermissionRequest"] = groups.ToArray();
            root["hooks"] = hooks;
            ClaudeCode.Save(path, root);
        }

        public static void Uninstall(string path)
        {
            if (!File.Exists(path)) return;
            var root = ClaudeCode.LoadForEdit(path);
            var hooks = Remove(root);
            if (hooks.Count == 0) root.Remove("hooks");
            else root["hooks"] = hooks;
            ClaudeCode.Save(path, root);
        }

        static IDictionary<string, object> Remove(IDictionary<string, object> root)
        {
            var hooks = ClaudeCode.Get(root, "hooks") as IDictionary<string, object> ?? new Dictionary<string, object>();
            foreach (var ev in hooks.Keys.ToList())
            {
                var groups = hooks[ev] as object[];
                if (groups == null) continue;
                var kept = groups.Where(g => !(g is IDictionary<string, object> && IsOurs((IDictionary<string, object>)g))).ToArray();
                if (kept.Length == 0) hooks.Remove(ev);
                else hooks[ev] = kept;
            }
            return hooks;
        }

        static bool IsOurs(IDictionary<string, object> group)
        {
            if (Json.Str(group, "name", "") == Name) return true;
            var handlers = ClaudeCode.Get(group, "hooks") as object[];
            return handlers != null && handlers.OfType<IDictionary<string, object>>().Any(h => Json.Str(h, "command", "").Contains(Flag));
        }

        // --codex-hook <event>: runs inside Codex for a moment. Works out whether the thread belongs to the
        // ChatGPT app or the CLI (from its rollout file) and posts that to the app. Never writes to stdout.
        public static int RunHook(int port, string eventName)
        {
            var body = HookFiles.ReadStdin();
            string ev = Json.Str(body, "hook_event_name", eventName ?? "");
            string transcript = Json.Str(body, "transcript_path", "");
            bool desktop;
            if (transcript.Length > 0 && File.Exists(transcript))
                desktop = Originator(transcript).IndexOf("desktop", StringComparison.OrdinalIgnoreCase) >= 0;
            else
                desktop = Process.GetProcessesByName("ChatGPT").Length > 0;
            var report = new Dictionary<string, object>
            {
                { "event", ev },
                { "instance", desktop ? CodexWatcher.AppInstance : CodexWatcher.CliInstance },
                { "tool", Json.Str(body, "tool_name", "") },
            };
            EventServer.Post(port, HookPath, Json.Write(report), 1500);
            return 0;
        }

        static string Originator(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var buffer = new byte[256 * 1024];
                    int n = fs.Read(buffer, 0, buffer.Length);
                    var m = Regex.Match(System.Text.Encoding.UTF8.GetString(buffer, 0, n), "\"originator\"\\s*:\\s*\"([^\"]*)\"");
                    return m.Success ? m.Groups[1].Value : "";
                }
            }
            catch (Exception) { return ""; }
        }
    }
}
