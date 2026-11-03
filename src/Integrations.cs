using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace OpaGachi
{
    // One place that knows what's installed and how each agent is connected, for the Agents and
    // Connections pages, the Welcome window and the tray menu. Read on the UI thread; cheap.
    sealed class AgentStatus
    {
        public DotKind Dot;
        public string Text = "";      // "Working · Claude Code", "Closed · appears when you open ChatGPT", "Not installed"
        public string Short = "";     // for the tray menu: "Working", "Idle", "Not open"
        public string Via = "";       // "CLI", "App", "CLI · App"
        public bool Active;           // has a live pet right now
    }

    // One row of Settings → Connections: a hook (by Id) the action button adds or removes.
    sealed class Connection
    {
        public string Id, Title, Badge, State, Action;
        public DotKind Dot;
        public bool ActionPrimary, ActionEnabled = true;
        public string Agent;
        public string Icon = "method-app", BadgeTip;
    }

    static class Integrations
    {
        static DateTime checkedAt = DateTime.MinValue;
        static HashSet<string> packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Store (MSIX) packages installed for this user, by family prefix ("OpenAI.Codex", "Claude").
        static bool HasPackage(string prefix)
        {
            Refresh();
            return packages.Any(p => p.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase));
        }

        static void Refresh()
        {
            if ((DateTime.UtcNow - checkedAt).TotalSeconds < 30) return;
            checkedAt = DateTime.UtcNow;
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                    if (key != null) foreach (var name in key.GetSubKeyNames()) found.Add(name);
            }
            catch (Exception) { }
            packages = found;
        }

        static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        public static bool ChatGptApp { get { return HasPackage("OpenAI.Codex") || Open("chatgpt"); } }
        public static bool ChatGptClassic { get { return HasPackage("OpenAI.ChatGPT-Desktop") || Open("chatgpt-classic"); } }
        public static bool ClaudeApp { get { return HasPackage("Claude") || Directory.Exists(Path.Combine(Local, "AnthropicClaude")) || Open("claude"); } }
        public static bool GeminiApp { get { return Directory.Exists(Path.Combine(Path.Combine(Local, "Google"), "Gemini")) || Open("gemini"); } }
        public static bool GrokApp { get { return File.Exists(Path.Combine(Path.Combine(Local, "Grok"), "grok-app.exe")) || Open("grok"); } }
        public static bool ClaudeCode { get { return Directory.Exists(Path.Combine(Sources.Home, ".claude")); } }
        public static bool Codex { get { return CodexWatcher.Installed; } }

        static AppWatcher.AppState App(string id)
        {
            AppWatcher.AppState s;
            return AppWatcher.States.TryGetValue(id, out s) ? s : null;
        }

        static bool Open(string id) { var s = App(id); return s != null && s.Open; }
        static bool Busy(string id) { var s = App(id); return s != null && s.Busy; }

        static bool Hooked(Func<bool> check)
        {
            try { return check(); }
            catch (Exception) { return false; }
        }

        public static bool ClaudeHooks { get { return Hooked(() => OpaGachi.ClaudeCode.HooksInstalled(OpaGachi.ClaudeCode.DefaultSettingsPath())); } }
        public static bool CodexHook { get { return Hooked(() => CodexHooks.Installed(CodexHooks.DefaultPath())); } }
        public static bool GeminiHook { get { return Hooked(() => GeminiHooks.Installed(GeminiHooks.DefaultPath())); } }
        // Agents installed on this PC whose OpaGachi hooks are off (the start-up reminder names them).
        public static List<string> MissingHooks()
        {
            var missing = new List<string>();
            if (ClaudeCode && !ClaudeHooks) missing.Add("Claude Code");
            if (Codex && !CodexHook) missing.Add("Codex");
            if (GeminiHooks.CliInstalled && !GeminiHook) missing.Add("Gemini CLI");
            if (GrokHooks.CliInstalled && !GrokHooks.Installed) missing.Add("Grok Build");
            return missing;
        }

        public static bool Relay { get { return Hooked(() => StatusLineRelay.Installed(OpaGachi.ClaudeCode.DefaultSettingsPath())); } }

        // ---- Per agent (Agents page, tray menu) ----------------------------------------------------

        public static AgentStatus StatusOf(string agent, AgentInfo info, PetManager pets)
        {
            var s = new AgentStatus();
            var mine = pets.Live.Where(p => p.AgentKey == agent && !p.InstanceKey.EndsWith(":preview", StringComparison.Ordinal)).ToList();
            bool working = mine.Any(p => p.IsBusy);
            bool needsYou = mine.Any(p => p.Mode == Mode.Help);
            var via = new List<string>();
            if (mine.Any(p => !p.NeedsWindow)) via.Add("CLI");
            if (mine.Any(p => p.NeedsWindow)) via.Add("App");
            s.Via = string.Join(" · ", via);
            s.Active = mine.Count > 0;

            if (info != null && !info.Enabled)
            {
                s.Dot = DotKind.Off;
                s.Text = s.Short = "Off";
                return s;
            }
            if (mine.Count > 0)
            {
                s.Dot = needsYou ? DotKind.NeedsYou : working ? DotKind.Working : DotKind.Connected;
                string what = needsYou ? "Needs you" : working ? "Working" : mine.Any(p => p.Mode == Mode.Complete) ? "Done" : "Idle";
                s.Short = what;
                s.Text = what + " · " + string.Join(", ", mine.Select(p => p.Label ?? p.Name).Distinct());
                return s;
            }
            bool installed;
            string hint;
            switch (agent)
            {
                case "claude":
                    installed = ClaudeCode || ClaudeApp;
                    hint = ClaudeApp && ClaudeCode ? "Closed · appears when you open Claude or start Claude Code"
                        : ClaudeApp ? "Closed · appears when you open Claude" : "Appears when you start Claude Code";
                    break;
                case "chatgpt":
                    installed = ChatGptApp || ChatGptClassic || Codex;
                    hint = "Closed · appears when you open ChatGPT";
                    break;
                case "gemini":
                    installed = GeminiApp || GeminiHooks.CliInstalled;
                    hint = "Closed · appears when you open Gemini";
                    break;
                case "grok":
                    installed = GrokHooks.CliInstalled || GrokApp;
                    hint = "Closed · appears when you open Grok";
                    break;
                default:
                    installed = false;
                    hint = "";
                    break;
            }
            if (!installed)
            {
                s.Dot = DotKind.NotInstalled;
                s.Short = "Not installed";
                s.Text = "Not installed";
                return s;
            }
            s.Dot = DotKind.Off;
            s.Text = hint;
            s.Short = "Not open";
            return s;
        }

        // ---- Connections page --------------------------------------------------------------------

        public static List<Connection> Connections(PetManager pets)
        {
            var list = new List<Connection>();
            int sessions = ClaudeSessions.LiveCount;
            bool hooks = ClaudeHooks;
            list.Add(Hook(new Connection
            {
                Id = "claude-code", Agent = "claude", Title = "Claude Code", Badge = "Sessions · Hooks", Icon = "method-hook",
                BadgeTip = "Claude Code's session files are read automatically. Hooks add instant approval alerts.",
                Dot = !ClaudeCode ? DotKind.NotInstalled : pets.Live.Any(p => p.AgentKey == "claude" && !p.NeedsWindow && p.IsBusy) ? DotKind.Working : sessions > 0 ? DotKind.Connected : DotKind.Off,
                State = !ClaudeCode ? "Not installed" : (sessions == 0 ? "No sessions" : sessions == 1 ? "1 session" : sessions + " sessions") + (hooks ? " · exact hooks on" : " · add hooks for approvals"),
                Action = hooks ? "Remove hooks" : "Add hooks", ActionPrimary = !hooks,
            }, ClaudeCode));
            bool relay = Relay;
            list.Add(Hook(new Connection
            {
                Id = "relay", Agent = "claude", Title = "Claude plan limits", Badge = "Status line", Icon = "nav-usage",
                BadgeTip = "Claude Code shares its 5-hour and weekly limits only with its status line. The relay passes them to the Usage page and leaves your status line as it is.",
                Dot = !ClaudeCode ? DotKind.NotInstalled : relay ? DotKind.Connected : DotKind.Off,
                State = !ClaudeCode ? "Not installed" : relay ? "Relay on · limits show on the Usage page" : "Add the relay to see 5-hour and weekly limits",
                Action = relay ? "Remove relay" : "Add relay", ActionPrimary = !relay,
            }, ClaudeCode));
            bool codexHook = CodexHook;
            int busyThreads = CodexWatcher.BusyThreads;
            list.Add(Hook(new Connection
            {
                Id = "codex", Agent = "chatgpt", Title = "Codex threads", Badge = "Logs · Hook", Icon = "method-logs",
                BadgeTip = "Codex thread logs are read automatically (ChatGPT app and CLI). The optional hook adds approval alerts.",
                Dot = !Codex ? DotKind.NotInstalled : busyThreads > 0 ? DotKind.Working : codexHook ? DotKind.Connected : DotKind.Detected,
                State = !Codex ? "Not installed" : (busyThreads > 0 ? "Working · " : "Watching logs · ") + (codexHook ? "approval hook on" : "approval hook off")
                    + (CodexUsage.Plan.Length > 0 ? " · " + char.ToUpperInvariant(CodexUsage.Plan[0]) + CodexUsage.Plan.Substring(1) + " plan" : ""),
                Action = codexHook ? "Remove hook" : "Add hook", ActionPrimary = !codexHook,
            }, Codex));
            bool geminiHook = GeminiHook;
            list.Add(Hook(new Connection
            {
                Id = "gemini-cli", Agent = "gemini", Title = "Gemini CLI", Badge = "CLI hook", Icon = "method-hook",
                BadgeTip = "Gemini CLI reports prompts, tool use and finished turns to OpaGachi through a hook.",
                Dot = !GeminiHooks.CliInstalled ? DotKind.NotInstalled : geminiHook ? DotKind.Connected : DotKind.Detected,
                State = !GeminiHooks.CliInstalled ? "Not installed" : geminiHook ? "Connected" : "Detected · hook not added",
                Action = geminiHook ? "Remove hook" : "Add hook", ActionPrimary = !geminiHook,
            }, GeminiHooks.CliInstalled));
            bool grokFile = GrokHooks.Installed;
            list.Add(Hook(new Connection
            {
                Id = "grok", Agent = "grok", Title = "Grok Build", Badge = "CLI hook", Icon = "method-hook",
                BadgeTip = "Grok Build reports its activity through its own hook file, or through the Claude Code hooks.",
                Dot = !GrokHooks.CliInstalled ? DotKind.NotInstalled : grokFile || hooks ? DotKind.Connected : DotKind.Detected,
                State = !GrokHooks.CliInstalled ? "Not installed" : grokFile ? "Connected" : hooks ? "Connected through the Claude Code hooks" : "Detected · hook not added",
                Action = grokFile ? "Remove hook" : "Add hook", ActionPrimary = !grokFile && !hooks,
            }, GrokHooks.CliInstalled));
            return list;
        }

        // Hook rows: primary "Add hook" when the tool is there, a true disabled "Not found" when it isn't.
        static Connection Hook(Connection c, bool toolInstalled)
        {
            if (!toolInstalled) { c.Action = "Not found"; c.ActionEnabled = false; c.ActionPrimary = false; }
            return c;
        }
    }
}
