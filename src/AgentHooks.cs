using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace OpaGachi
{
    // Optional hooks for terminal agents other than Claude Code and Codex, plus the Claude Code status
    // line relay. Every installer keeps a .opagachi-backup of the file it edits and only removes its own
    // entries. All of them are user-triggered (Settings → Connections), never automatic.

    // Gemini CLI: command hooks in ~/.gemini/settings.json. Gemini CLI parses a hook's stdout as JSON,
    // so the handler prints "{}" (no decision) and Gemini carries on as usual.
    static class GeminiHooks
    {
        public const string HookPath = "/gemini-cli";
        public const string Agent = "gemini";
        const string Name = "opagachi";
        const string Flag = "--gemini-hook";
        static readonly string[] Events = { "SessionStart", "SessionEnd", "BeforeAgent", "AfterAgent", "BeforeTool", "AfterTool", "Notification" };

        public static string Dir { get { return Path.Combine(Sources.Home, ".gemini"); } }
        public static string DefaultPath() { return Path.Combine(Dir, "settings.json"); }
        public static bool CliInstalled { get { return Directory.Exists(Dir); } }

        public static bool Installed(string path)
        {
            var root = Json.ReadFile(path);
            var hooks = root == null ? null : ClaudeCode.Get(root, "hooks") as IDictionary<string, object>;
            return hooks != null && hooks.Values.OfType<object[]>().SelectMany(g => g).OfType<IDictionary<string, object>>().Any(IsOurs);
        }

        public static void Install(string path, string exePath)
        {
            var root = ClaudeCode.LoadForEdit(path);
            var hooks = HookFiles.RemoveGroups(root, IsOurs);
            foreach (var ev in Events)
            {
                var groups = ClaudeCode.GroupList(hooks, ev);
                groups.Add(new Dictionary<string, object>
                {
                    { "hooks", new object[] { new Dictionary<string, object> {
                        { "name", Name }, { "type", "command" }, { "command", "\"" + exePath + "\" " + Flag }, { "timeout", 5000 } } } },
                });
                hooks[ev] = groups.ToArray();
            }
            root["hooks"] = hooks;
            ClaudeCode.Save(path, root);
            FactoryReset.RegisterHookPath("gemini", path);
        }

        public static void Uninstall(string path)
        {
            if (!File.Exists(path)) return;
            var root = ClaudeCode.LoadForEdit(path);
            var hooks = HookFiles.RemoveGroups(root, IsOurs);
            if (hooks.Count == 0) root.Remove("hooks");
            else root["hooks"] = hooks;
            ClaudeCode.Save(path, root);
        }

        static bool IsOurs(IDictionary<string, object> group)
        {
            var handlers = ClaudeCode.Get(group, "hooks") as object[];
            return handlers != null && handlers.OfType<IDictionary<string, object>>()
                .Any(h => Json.Str(h, "name", "") == Name || Json.Str(h, "command", "").Contains(Flag));
        }

        // --gemini-hook: runs inside Gemini CLI for each event. Adds the terminal window on session
        // start and prompts, posts to the app, prints "{}".
        public static int RunHook(int port)
        {
            var body = HookFiles.ReadStdin();
            string ev = Json.Str(body, "hook_event_name", "");
            var report = new Dictionary<string, object>
            {
                { "event", ev },
                { "session", Json.Str(body, "session_id", "") },
                { "tool", Json.Str(body, "tool_name", "") },
                { "notification", Json.Str(body, "notification_type", "") },
                { "error", HasError(body) },
            };
            if (ev == "SessionStart" || ev == "BeforeAgent")
            {
                int pid;
                IntPtr host = HookFiles.HostFromAncestors(new[] { "gemini.exe", "node.exe", "bun.exe" }, out pid);
                report["host"] = host.ToInt64();
                report["pid"] = pid;
            }
            EventServer.Post(port, HookPath, Json.Write(report), 1000);
            Console.Out.Write("{}");
            return 0;
        }

        static bool HasError(IDictionary<string, object> body)
        {
            object r;
            var response = body != null && body.TryGetValue("tool_response", out r) ? r as IDictionary<string, object> : null;
            return response != null && response.ContainsKey("error") && response["error"] != null && Json.Str(response, "error", "x").Length > 0;
        }

        // Gemini CLI tool names: reading and searching count as thinking.
        public static Signal ClassifyTool(string tool)
        {
            string t = tool.ToLowerInvariant();
            if (t.Contains("image")) return Signal.ImageGen;
            if (t.StartsWith("read") || t.Contains("search") || t.Contains("glob") || t.Contains("list") || t.Contains("fetch") || t.Contains("memory") || t.Contains("todo"))
                return Signal.Think;
            return Signal.Work;
        }
    }

    // Grok Build (xAI's terminal agent): personal hooks live in their own files under ~/.grok/hooks/, and
    // HTTP hooks are supported, so this writes ~/.grok/hooks/opagachi.json and edits nothing else.
    // Grok Build also reads Claude Code's hook settings, so with the Claude hooks installed Grok sessions
    // reach the app even without this file (their payloads use camelCase field names).
    static class GrokHooks
    {
        public const string HookPath = "/grok";
        public const string Agent = "grok";
        static readonly string[] Events =
        {
            "SessionStart", "SessionEnd", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure",
            "PermissionDenied", "Notification", "Stop", "StopFailure",
        };

        public static string Dir { get { return Path.Combine(Sources.Home, ".grok"); } }
        public static string FilePath { get { return Path.Combine(Path.Combine(Dir, "hooks"), "opagachi.json"); } }
        public static bool CliInstalled { get { return Directory.Exists(Dir); } }
        static string Legacy { get { return Path.Combine(Path.Combine(Dir, "hooks"), "opa-pet.json"); } }   // written before the OpaGachi rename; same HTTP hooks
        public static bool Installed { get { return File.Exists(FilePath) || File.Exists(Legacy); } }

        public static void Install(int port)
        {
            var hooks = new Dictionary<string, object>();
            foreach (var ev in Events)
                hooks[ev] = new object[] { new Dictionary<string, object> {
                    { "hooks", new object[] { new Dictionary<string, object> { { "type", "http" }, { "url", "http://127.0.0.1:" + port + HookPath }, { "timeout", 2 } } } } } };
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            Json.WriteFile(FilePath, new Dictionary<string, object> { { "hooks", hooks } });
            if (File.Exists(Legacy)) File.Delete(Legacy);
        }

        public static void Uninstall() { foreach (var f in new[] { FilePath, Legacy }) if (File.Exists(f)) File.Delete(f); }

        // Grok Build event -> pet signals (same event names as Claude Code; camelCase fields).
        public static List<Signal> Map(IDictionary<string, object> body, bool waitingOnUser, out bool closed)
        {
            var signals = new List<Signal>();
            closed = false;
            string ev = Json.Str(body, "hookEventName", Json.Str(body, "hook_event_name", ""));
            string tool = Json.Str(body, "toolName", Json.Str(body, "tool_name", ""));
            string normalized = ev.Replace("_", "");
            ev = Events.FirstOrDefault(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase)) ?? ev;
            switch (ev)
            {
                case "SessionStart": signals.Add(Signal.Opened); break;
                case "SessionEnd": closed = true; break;
                case "UserPromptSubmit": signals.Add(Signal.UserPrompted); break;
                case "PreToolUse":
                    if (waitingOnUser) signals.Add(Signal.Approved);
                    signals.Add(ClaudeCode.AskTools.Contains(tool) ? Signal.NeedsApproval : ClaudeCode.ClassifyTool(tool));
                    break;
                case "PostToolUse": signals.Add(Signal.Think); break;
                case "PostToolUseFailure":
                case "PermissionDenied": signals.Add(Signal.Error); break;
                case "Notification":
                    string text = (Json.Str(body, "message", "") + " " + Json.Str(body, "notificationType", "")).ToLowerInvariant();
                    if (text.Contains("permission") || text.Contains("approv") || text.Contains("allow") || text.Contains("waiting")) signals.Add(Signal.NeedsApproval);
                    break;
                case "Stop": signals.Add(Signal.Done); break;
                case "StopFailure": signals.Add(Signal.FatalError); break;
            }
            return signals;
        }

        public static string InstanceKey(IDictionary<string, object> body)
        {
            string session = Json.Str(body, "sessionId", Json.Str(body, "session_id", null));
            return session == null ? null : "grok-build:" + session;
        }

        public static bool IsGrokPayload(IDictionary<string, object> body)
        {
            string ev = Json.Str(body, "hook_event_name", "");
            return body != null && (body.ContainsKey("hookEventName") || body.ContainsKey("sessionId") || Json.Str(body,"agent","") == "grok" || (ev.Length > 0 && char.IsLower(ev[0])));
        }
    }

    // Claude Code's plan limits (5-hour and weekly) only reach status line scripts. The relay sits in
    // front of the user's own status line: Claude Code runs `OpaGachi.exe --statusline <original>`, which
    // forwards the JSON to the app, then runs the original command with the same input and passes its
    // output through untouched. The original command is kept inside the relay command (base64), so
    // removing the relay restores it exactly.
    static class StatusLineRelay
    {
        public const string Path_ = "/claude-status";
        const string Flag = "--statusline";

        public static bool Installed(string settingsPath)
        {
            var root = Json.ReadFile(settingsPath);
            var line = root == null ? null : ClaudeCode.Get(root, "statusLine") as IDictionary<string, object>;
            return line != null && Json.Str(line, "command", "").Contains(Flag);
        }

        public static void Install(string settingsPath, string exePath)
        {
            var root = ClaudeCode.LoadForEdit(settingsPath);
            var line = ClaudeCode.Get(root, "statusLine") as IDictionary<string, object>;
            string original = line == null ? "" : Json.Str(line, "command", "");
            if (original.Contains(Flag)) return;   // already relayed
            string packed = original.Length == 0 ? "-" : Convert.ToBase64String(Encoding.UTF8.GetBytes(original));
            if (line == null) line = new Dictionary<string, object> { { "type", "command" } };
            line["command"] = "\"" + exePath + "\" " + Flag + " " + packed;
            root["statusLine"] = line;
            ClaudeCode.Save(settingsPath, root);
            FactoryReset.RegisterHookPath("claude", settingsPath);
        }

        public static void Uninstall(string settingsPath)
        {
            if (!File.Exists(settingsPath)) return;
            var root = ClaudeCode.LoadForEdit(settingsPath);
            var line = ClaudeCode.Get(root, "statusLine") as IDictionary<string, object>;
            if (line == null) return;
            string command = Json.Str(line, "command", "");
            int at = command.IndexOf(Flag, StringComparison.Ordinal);
            if (at < 0) return;
            string original = Unpack(command.Substring(at + Flag.Length).Trim());
            if (original.Length == 0) root.Remove("statusLine");
            else line["command"] = original;
            ClaudeCode.Save(settingsPath, root);
        }

        static string Unpack(string packed)
        {
            if (packed.Length == 0 || packed == "-") return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(packed)); }
            catch (FormatException) { return ""; }
        }

        // Runs as Claude Code's status line command. Must stay fast and never break the user's line.
        public static int Run(int port, string packed)
        {
            byte[] input;
            using (var stdin = Console.OpenStandardInput())
            using (var ms = new MemoryStream())
            {
                stdin.CopyTo(ms);
                input = ms.ToArray();
            }
            try { EventServer.Post(port, Path_, Encoding.UTF8.GetString(input).TrimStart('﻿'), 300); }
            catch (Exception) { }
            string original = Unpack(packed);
            if (original.Length == 0) return 0;
            var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + original + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true,
            };
            using (var p = Process.Start(psi))
            {
                p.StandardInput.BaseStream.Write(input, 0, input.Length);
                p.StandardInput.Close();
                using (var stdout = Console.OpenStandardOutput()) p.StandardOutput.BaseStream.CopyTo(stdout);
                p.WaitForExit(10000);
                return p.HasExited ? p.ExitCode : 0;
            }
        }
    }

    // Shared bits for hook handlers and hook files.
    static class HookFiles
    {
        // Hook input is UTF-8 JSON. Read raw bytes (Console.In would decode with the console's legacy code
        // page, mangling non-ASCII paths and byte-order marks).
        public static string ReadStdinText()
        {
            try
            {
                using (var stdin = Console.OpenStandardInput())
                using (var ms = new MemoryStream())
                {
                    stdin.CopyTo(ms);
                    return new UTF8Encoding(false).GetString(ms.ToArray()).TrimStart('﻿');
                }
            }
            catch (Exception) { return ""; }
        }

        public static IDictionary<string, object> ReadStdin()
        {
            string input = ReadStdinText();
            try { return input.Length == 0 ? null : Json.Parse(input); }
            catch (Exception) { return null; }
        }

        // Removes hook groups matching `ours` from root["hooks"]; returns the remaining hooks object.
        public static IDictionary<string, object> RemoveGroups(IDictionary<string, object> root, Func<IDictionary<string, object>, bool> ours)
        {
            var hooks = ClaudeCode.Get(root, "hooks") as IDictionary<string, object> ?? new Dictionary<string, object>();
            foreach (var ev in hooks.Keys.ToList())
            {
                var groups = hooks[ev] as object[];
                if (groups == null) continue;
                var kept = groups.Where(g => !(g is IDictionary<string, object> && ours((IDictionary<string, object>)g))).ToArray();
                if (kept.Length == 0) hooks.Remove(ev);
                else hooks[ev] = kept;
            }
            return hooks;
        }

        // The terminal window of the agent process among this hook process's ancestors.
        public static IntPtr HostFromAncestors(string[] agentNames, out int agentPid)
        {
            agentPid = 0;
            foreach (var p in ClaudeCode.AncestorChain())
            {
                if (!agentNames.Contains(p.Value, StringComparer.OrdinalIgnoreCase)) continue;
                agentPid = p.Key;
                string how;
                return ClaudeCode.ConsoleHost(p.Key, out how);
            }
            return IntPtr.Zero;
        }
    }
}
