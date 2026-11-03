using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OpaGachi
{
    // Token usage for the Usage page. Everything comes from files the agents already write locally:
    //  * Claude Code: every assistant message in a transcript carries its token usage.
    //  * Claude plan limits (5-hour / weekly %): only Claude Code's status line gets these, via the
    //    optional relay (StatusLineRelay); stored here when it reports.
    //  * Codex / ChatGPT app: token_count events in the thread logs carry token totals and the plan's
    //    5-hour and weekly limits.
    //  * Grok: each session's usage.json (Grok Build, and the Grok app's agent sessions) carries tokens and cost
    //    per turn. Grok never writes its plan limits anywhere other apps can read; they stay in its /usage screen.
    //  * Gemini: the app is a web page in a window and keeps no usage on the PC, so OpaGachi counts the answers it
    //    sees instead (AnswerLog).

    sealed class LimitWindow
    {
        public double UsedPercent = -1;     // 0..100, -1 unknown
        public int WindowMinutes;
        public DateTime ResetsAt;           // local time; MinValue if unknown
    }

    // Claude's plan limits, from the status line relay.
    static class ClaudeUsage
    {
        public static LimitWindow FiveHour = new LimitWindow(), Weekly = new LimitWindow();
        public static DateTime LimitsSeen = DateTime.MinValue;

        // Status line JSON from the relay: {"rate_limits":{"five_hour":{"used_percentage":..,"resets_at":..},"seven_day":{..}}, ...}
        public static void FromStatusLine(IDictionary<string, object> body)
        {
            object v;
            var limits = body.TryGetValue("rate_limits", out v) ? v as IDictionary<string, object> : null;
            if (limits == null) return;
            FiveHour = Window(limits, "five_hour", 300);
            Weekly = Window(limits, "seven_day", 10080);
            LimitsSeen = DateTime.Now;
        }

        static LimitWindow Window(IDictionary<string, object> limits, string key, int minutes)
        {
            object v;
            var w = limits.TryGetValue(key, out v) ? v as IDictionary<string, object> : null;
            var result = new LimitWindow { WindowMinutes = minutes };
            if (w == null) return result;
            result.UsedPercent = Json.Num(w, "used_percentage", Json.Num(w, "used_percent", -1));
            double resets = Json.Num(w, "resets_at", Json.Num(w, "reset_at", 0));
            if (resets > 0) result.ResetsAt = Epoch(resets);
            return result;
        }

        public static DateTime Epoch(double seconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();
        }
    }

    // Codex / ChatGPT plan limits and plan name, from the thread logs' token_count events.
    static class CodexUsage
    {
        public static LimitWindow FiveHour = new LimitWindow(), Weekly = new LimitWindow();
        public static DateTime LimitsSeen = DateTime.MinValue;
        public static string Plan = "";

        // One token_count event: {"info":{...}, "rate_limits":{"primary":{...},"secondary":{...},"plan_type":"plus"}}
        public static void Record(string rollout, IDictionary<string, object> payload) { Record(payload, DateTime.Now); }

        static void Record(IDictionary<string, object> payload, DateTime when)
        {
            if (payload == null) return;
            object v;
            var limits = payload.TryGetValue("rate_limits", out v) ? v as IDictionary<string, object> : null;
            if (limits == null) return;
            FiveHour = Window(limits, "primary");
            Weekly = Window(limits, "secondary");
            Plan = Json.Str(limits, "plan_type", Plan);
            LimitsSeen = when;
        }

        static LimitWindow Window(IDictionary<string, object> limits, string key)
        {
            object v;
            var w = limits.TryGetValue(key, out v) ? v as IDictionary<string, object> : null;
            var result = new LimitWindow();
            if (w == null) return result;
            result.UsedPercent = Json.Num(w, "used_percent", -1);
            result.WindowMinutes = (int)Json.Num(w, "window_minutes", 0);
            double resets = Json.Num(w, "resets_at", 0);
            if (resets > 0) result.ResetsAt = ClaudeUsage.Epoch(resets);
            return result;
        }

        // At start-up: the limits in the newest thread's last token_count.
        public static void Seed(string sessions)
        {
            if (!Directory.Exists(sessions)) return;
            var newest = new DirectoryInfo(sessions).GetFiles("rollout-*.jsonl", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            if (newest == null) return;
            string last = LastTokenCount(newest.FullName);
            if (last == null) return;
            try
            {
                var o = Json.Parse(last);
                object p;
                DateTime when;
                if (!DateTime.TryParse(Json.Str(o, "timestamp", ""), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out when)) when = newest.LastWriteTimeUtc;
                if (o != null && o.TryGetValue("payload", out p)) Record(p as IDictionary<string, object>, when.ToLocalTime());
            }
            catch (Exception) { }
        }

        static string LastTokenCount(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long size = Math.Min(fs.Length, 512 * 1024);
                fs.Position = fs.Length - size;
                var buffer = new byte[size];
                int n = fs.Read(buffer, 0, (int)size);
                var text = Encoding.UTF8.GetString(buffer, 0, n);
                int at = text.LastIndexOf("\"type\":\"token_count\"", StringComparison.Ordinal);
                if (at < 0) return null;
                int start = text.LastIndexOf('\n', at) + 1;
                int end = text.IndexOf('\n', at);
                return end < 0 ? null : text.Substring(start, end - start);
            }
        }
    }

    // Tokens per local day for the last 30 days, for the Usage heat maps (user, 2026-10-31). Every Claude Code
    // transcript and Codex thread log touched in that window is read once, then only what's appended. Files
    // are keyed by name, so a Codex thread moved to archived_sessions isn't counted twice.
    static class UsageHistory
    {
        public const int Days = 30;
        static readonly object Gate = new object();
        static readonly Dictionary<string, FileState> files = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<DateTime, long> claude = new Dictionary<DateTime, long>(), codex = new Dictionary<DateTime, long>(),
            grok = new Dictionary<DateTime, long>(), grokCost = new Dictionary<DateTime, long>();
        static Timer timer;
        static volatile bool scanning;
        static bool firstDone;

        sealed class FileState { public long Offset, LastTokens, Total; public string LastId; public DateTime LastDay; }

        static readonly Regex IdRx = new Regex("\"id\":\"(msg_[^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex TimeRx = new Regex("\"timestamp\":\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex InRx = new Regex("\"input_tokens\":(\\d+)", RegexOptions.Compiled);
        static readonly Regex OutRx = new Regex("\"output_tokens\":(\\d+)", RegexOptions.Compiled);
        static readonly Regex TotalRx = new Regex("\"total_token_usage\":\\{[^}]*\"total_tokens\":(\\d+)", RegexOptions.Compiled);

        public static void Start() { timer = new Timer(_ => Scan(), null, 4000, 60000); }
        public static void Stop() { if (timer != null) timer.Dispose(); }

        // Oldest day first; the last entry is today.
        public static long[] Claude { get { return Series(claude); } }
        public static long[] Codex { get { return Series(codex); } }
        public static long[] Grok { get { return Series(grok); } }
        public static long[] GrokCost { get { return Series(grokCost); } }   // 10^10 ticks per USD

        public static long[] Series(Dictionary<DateTime, long> days)
        {
            var result = new long[Days];
            var first = DateTime.Today.AddDays(1 - Days);
            lock (Gate)
                for (int i = 0; i < Days; i++) { long v; if (days.TryGetValue(first.AddDays(i), out v)) result[i] = v; }
            return result;
        }

        static void Scan()
        {
            if (scanning) return;
            scanning = true;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var cutoff = DateTime.Today.AddDays(1 - Days);
                lock (Gate)
                    foreach (var d in new[] { claude, codex })
                        foreach (var old in d.Keys.Where(k => k < cutoff).ToList()) d.Remove(old);
                string projects = Path.Combine(Path.Combine(Sources.Home, ".claude"), "projects");
                if (Directory.Exists(projects))
                    foreach (var file in Directory.GetFiles(projects, "*.jsonl", SearchOption.AllDirectories))
                        Read(file, cutoff, false);
                string home = Environment.GetEnvironmentVariable("CODEX_HOME");
                string root = string.IsNullOrEmpty(home) ? Path.Combine(Sources.Home, ".codex") : home;
                foreach (var dir in new[] { "sessions", "archived_sessions" })
                    if (Directory.Exists(Path.Combine(root, dir)))
                        foreach (var file in Directory.GetFiles(Path.Combine(root, dir), "rollout-*.jsonl", SearchOption.AllDirectories))
                            Read(file, cutoff, true);
                string grokSessions = Path.Combine(Path.Combine(Sources.Home, ".grok"), "sessions");
                if (Directory.Exists(grokSessions)) ReadGrok(grokSessions, cutoff);
            }
            catch (Exception e) { Log.Write("usage history: " + e.Message); }
            finally
            {
                if (!firstDone) { firstDone = true; Log.Write("usage history: first scan " + watch.ElapsedMilliseconds + " ms"); }
                scanning = false;
            }
        }

        static void Read(string file, DateTime cutoff, bool isCodex)
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff) return;
                string key = Path.GetFileName(file);
                FileState s;
                lock (Gate) if (!files.TryGetValue(key, out s)) files[key] = s = new FileState();
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length < s.Offset) { s.Offset = 0; s.Total = 0; s.LastId = null; }
                    if (fs.Length == s.Offset) return;
                    fs.Position = s.Offset;
                    var reader = new StreamReader(fs, Encoding.UTF8, false, 1 << 16);
                    long consumed = s.Offset;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        consumed += Encoding.UTF8.GetByteCount(line) + 1;
                        if (isCodex) ReadCodex(line, s, cutoff); else ReadClaude(line, s, cutoff);
                    }
                    s.Offset = Math.Min(consumed, fs.Length);
                }
            }
            catch (IOException) { }
        }

        static DateTime Day(string line)
        {
            var m = TimeRx.Match(line);
            DateTime stamp;
            if (!m.Success || !DateTime.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out stamp)) return DateTime.MinValue;
            return stamp.ToLocalTime().Date;
        }

        static void Add(Dictionary<DateTime, long> days, DateTime day, long n, DateTime cutoff)
        {
            if (day < cutoff || n == 0) return;
            long v;
            lock (Gate) { days.TryGetValue(day, out v); days[day] = v + n; }
        }

        // Input + output per assistant message, as on the rest of the page. A message spans several lines
        // with the same id; only the change in its usage is added.
        // shortcut: assumes a message's lines are consecutive in the file (they are in Claude Code today); a per-file id set if that changes.
        static void ReadClaude(string line, FileState s, DateTime cutoff)
        {
            if (line.IndexOf("\"output_tokens\"", StringComparison.Ordinal) < 0 || line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) < 0) return;
            var id = IdRx.Match(line);
            if (!id.Success) return;
            long tokens = Last(InRx, line) + Last(OutRx, line);
            if (id.Groups[1].Value == s.LastId) { Add(claude, s.LastDay, tokens - s.LastTokens, cutoff); s.LastTokens = tokens; return; }
            var day = Day(line);
            if (day == DateTime.MinValue) return;
            Add(claude, day, tokens, cutoff);
            s.LastId = id.Groups[1].Value; s.LastTokens = tokens; s.LastDay = day;
        }

        // Codex logs a running total per thread; each event adds its growth to that day.
        static void ReadCodex(string line, FileState s, DateTime cutoff)
        {
            if (line.IndexOf("\"token_count\"", StringComparison.Ordinal) < 0) return;
            var m = TotalRx.Match(line);
            long total;
            if (!m.Success || !long.TryParse(m.Groups[1].Value, out total)) return;
            long grew = total >= s.Total ? total - s.Total : total;
            s.Total = total;
            Add(codex, Day(line), grew, cutoff);
        }

        static long Last(Regex rx, string line)
        {
            var all = rx.Matches(line);
            long v;
            return all.Count > 0 && long.TryParse(all[all.Count - 1].Groups[1].Value, out v) ? v : 0;
        }

        // Grok rewrites a session's usage.json (the documented `grok usage` envelope) after every turn, so a file is
        // re-read only when it changes and the daily totals are rebuilt from every file's turns. A forked session
        // repeats its parent's turns, so a turn is counted once by its end time and tokens.
        sealed class GrokTurn { public string Key; public DateTime Day; public long Tokens, Ticks; }
        sealed class GrokFile { public DateTime Written; public readonly List<GrokTurn> Turns = new List<GrokTurn>(); }
        static readonly Dictionary<string, GrokFile> grokFiles = new Dictionary<string, GrokFile>(StringComparer.OrdinalIgnoreCase);
        static readonly Regex Fraction = new Regex(@"(\.\d{7})\d+", RegexOptions.Compiled);   // .NET parses at most 7 digits

        static void ReadGrok(string sessions, DateTime cutoff)
        {
            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(sessions, "usage.json", SearchOption.AllDirectories))
            {
                var written = File.GetLastWriteTime(file);
                if (written < cutoff) continue;
                current.Add(file);
                GrokFile cached;
                if (grokFiles.TryGetValue(file, out cached) && cached.Written == written) continue;
                try
                {
                    string text;
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(fs, Encoding.UTF8)) text = reader.ReadToEnd();
                    var root = Json.Parse(text);
                    object turns;
                    if (root == null || !root.TryGetValue("turns", out turns) || !(turns is object[])) continue;   // mid-write: next scan
                    var g = new GrokFile { Written = written };
                    foreach (var turn in ((object[])turns).OfType<IDictionary<string, object>>())
                    {
                        string end = Json.Str(turn, "endedAt", "");
                        DateTimeOffset ended;
                        if (!DateTimeOffset.TryParse(Fraction.Replace(end, "$1"), CultureInfo.InvariantCulture, DateTimeStyles.None, out ended)) continue;
                        long tokens = (long)Json.Num(turn, "totalTokens", 0);
                        g.Turns.Add(new GrokTurn { Key = end + "|" + tokens, Day = ended.LocalDateTime.Date, Tokens = tokens, Ticks = (long)Json.Num(turn, "costUsdTicks", 0) });
                    }
                    grokFiles[file] = g;
                }
                catch (Exception) { }   // being written: next scan
            }
            foreach (var gone in grokFiles.Keys.Where(k => !current.Contains(k)).ToList()) grokFiles.Remove(gone);
            var counted = new HashSet<string>();
            lock (Gate)
            {
                grok.Clear();
                grokCost.Clear();
                foreach (var t in grokFiles.Values.SelectMany(f => f.Turns))
                    if (t.Day >= cutoff && counted.Add(t.Key)) { Bump(grok, t.Day, t.Tokens); Bump(grokCost, t.Day, t.Ticks); }
            }
        }

        static void Bump(Dictionary<DateTime, long> days, DateTime day, long n) { long v; days.TryGetValue(day, out v); days[day] = v + n; }
    }

    // Answers per local day, counted by OpaGachi itself when a pet finishes a reply (user, 2026-11-02): the only
    // usage measure for agents that keep none on the PC, such as the Gemini app. Kept for 30 days in the data folder.
    static class AnswerLog
    {
        static readonly object Gate = new object();
        static Dictionary<string, Dictionary<DateTime, long>> agents;
        static string FilePath { get { return Path.Combine(Program.Data, "answers.json"); } }

        public static void Record(string agent)
        {
            lock (Gate)
            {
                var days = Days(agent);
                long n;
                days.TryGetValue(DateTime.Today, out n);
                days[DateTime.Today] = n + 1;
                var cutoff = DateTime.Today.AddDays(1 - UsageHistory.Days);
                foreach (var old in days.Keys.Where(k => k < cutoff).ToList()) days.Remove(old);
                var json = agents.ToDictionary(a => a.Key, a => (object)a.Value.ToDictionary(d => d.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d => (object)d.Value));
                try { Directory.CreateDirectory(Program.Data); Json.WriteFile(FilePath, json); }
                catch (Exception e) { Log.Write("answers: " + e.Message); }
            }
        }

        // Oldest day first; the last entry is today.
        public static long[] Series(string agent) { lock (Gate) return UsageHistory.Series(Days(agent)); }

        static Dictionary<DateTime, long> Days(string agent)
        {
            if (agents == null)
            {
                agents = new Dictionary<string, Dictionary<DateTime, long>>(StringComparer.OrdinalIgnoreCase);
                IDictionary<string, object> saved = null;
                try { saved = Json.ReadFile(FilePath); } catch (Exception e) { Log.Write("answers: " + e.Message); }
                if (saved != null)
                    foreach (var a in saved)
                    {
                        var days = a.Value as IDictionary<string, object>;
                        if (days == null) continue;
                        var d = agents[a.Key] = new Dictionary<DateTime, long>();
                        foreach (var kv in days)
                        {
                            DateTime day;
                            if (DateTime.TryParseExact(kv.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) d[day] = (long)Json.Num(days, kv.Key, 0);
                        }
                    }
            }
            Dictionary<DateTime, long> result;
            if (!agents.TryGetValue(agent, out result)) agents[agent] = result = new Dictionary<DateTime, long>();
            return result;
        }
    }

    static class UsageText
    {
        public static string Tokens(long n)
        {
            if (n >= 1000000000) return (n / 1e9).ToString("0.0#", CultureInfo.InvariantCulture) + " B";
            if (n >= 1000000) return (n / 1e6).ToString("0.0#", CultureInfo.InvariantCulture) + " M";
            if (n >= 10000) return (n / 1e3).ToString("0", CultureInfo.InvariantCulture) + " k";
            if (n >= 1000) return (n / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + " k";
            return n.ToString(CultureInfo.InvariantCulture);
        }

        // Grok's cost ticks (10^10 per USD) as "$0.85", or "under $0.01".
        public static string Dollars(long ticks)
        {
            double usd = ticks / 1e10;
            return usd < 0.01 ? "under $0.01" : "$" + usd.ToString("0.00", CultureInfo.InvariantCulture);
        }

        // How full a limit is, the same for every agent and window (user, 2026-10-31): green below 50%,
        // yellow to 74%, orange to 89%, red from 90%.
        public static System.Drawing.Color LevelColor(double percent)
        {
            return percent >= 90 ? Theme.LevelRed : percent >= 75 ? Theme.LevelOrange : percent >= 50 ? Theme.LevelYellow : Theme.LevelGreen;
        }

        // Gauge captions: "resets in 1h 18m", "resets in 42m", "resets Thu 21:00".
        public static string ResetsShort(DateTime at)
        {
            if (at == DateTime.MinValue) return "";
            var span = at - DateTime.Now;
            if (span.TotalMinutes < 1) return "resets now";
            if (span.TotalHours < 1) return "resets in " + (int)span.TotalMinutes + "m";
            if (span.TotalHours < 24) return "resets in " + (int)span.TotalHours + "h " + span.Minutes + "m";
            return "resets " + at.ToString("ddd HH:mm", CultureInfo.InvariantCulture);
        }

        public static string Ago(DateTime at)
        {
            if (at == DateTime.MinValue) return "never";
            var span = DateTime.Now - at;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return (int)span.TotalMinutes + " min ago";
            if (span.TotalDays < 1) return (int)span.TotalHours + " h ago";
            return at.ToString("MMM d, HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
