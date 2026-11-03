using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace OpaGachi
{
    // Claude Code with nothing installed. Every interactive session keeps ~/.claude/sessions/<pid>.json
    // up to date (including a busy/idle status) and appends its conversation to
    // ~/.claude/projects/<folder>/<session>.jsonl. Following both gives the pet's state.
    // When the HTTP hooks are installed they win: PetManager ignores these signals for any session it
    // has heard a hook from, but still uses the session list for arrivals and departures.
    sealed class ClaudeSessions
    {
        public const string Origin = "claude-transcript";
        public static volatile int LiveCount;

        readonly string sessionsDir, projectsDir;
        readonly Thread thread;
        volatile bool stopped;
        readonly Dictionary<int, Session> live = new Dictionary<int, Session>();
        readonly HashSet<string> loggedUnknown = new HashSet<string>();

        sealed class Session
        {
            public int Pid;
            public string Id, Instance, Status = "", Entry = "";
            public DateTime FileTime;
            public JsonlTail Tail;
            public DateTime NextTranscriptLook;
            public bool Busy;
        }

        public ClaudeSessions()
        {
            string claude = Path.Combine(Sources.Home, ".claude");
            sessionsDir = Path.Combine(claude, "sessions");
            projectsDir = Path.Combine(claude, "projects");
            thread = new Thread(Run) { IsBackground = true, Name = "claude-sessions", Priority = ThreadPriority.BelowNormal };
        }

        public static bool IsLive(string id)
        {
            string dir = Path.Combine(Path.Combine(Sources.Home, ".claude"), "sessions");
            if (!Directory.Exists(dir)) return false;
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                var json = Json.ReadFile(file);
                if (Json.Str(json, "sessionId", "") == id && Json.Str(json,"kind","interactive") == "interactive" && Proc.Alive((int)Json.Num(json,"pid",0))) return true;
            }
            return false;
        }

        public void Start() { thread.Start(); }
        public void Stop() { stopped = true; }

        void Run()
        {
            DateTime nextScan = DateTime.MinValue;
            while (!stopped)
            {
                long t0 = Stopwatch.GetTimestamp();
                try
                {
                    if (DateTime.UtcNow >= nextScan)
                    {
                        nextScan = DateTime.UtcNow.AddSeconds(1.5);
                        Scan();
                    }
                    foreach (var s in live.Values) Follow(s);
                }
                catch (Exception e) { Log.Write("claude sessions: " + e.Message); }
                Interlocked.Add(ref Sources.ClaudeTicks, Stopwatch.GetTimestamp() - t0);
                Thread.Sleep(400);
            }
            foreach (var s in live.Values) if (s.Tail != null) s.Tail.Dispose();
        }

        void Scan()
        {
            if (!Directory.Exists(sessionsDir))
            {
                Sources.Status["claude-code"] = "Claude Code not found";
                return;
            }
            var seen = new HashSet<int>();
            foreach (var file in Directory.GetFiles(sessionsDir, "*.json"))
            {
                int pid;
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out pid)) continue;
                seen.Add(pid);
                Session s;
                live.TryGetValue(pid, out s);
                DateTime time;
                try { time = File.GetLastWriteTimeUtc(file); }
                catch (Exception) { continue; }
                if (s != null && time == s.FileTime) continue;
                if (s == null && !Proc.Alive(pid)) continue;   // left behind by a session that crashed

                IDictionary<string, object> json;
                try { json = Json.Parse(File.ReadAllText(file)); }
                catch (Exception) { continue; }                // mid-write; next scan gets it
                string id = Json.Str(json, "sessionId", null);
                if (id == null) continue;
                string kind = Json.Str(json, "kind", "interactive");
                if (kind != "interactive") continue;           // claude -p / SDK runs: no pet

                if (s != null && s.Id != id)
                {
                    End(s);                                    // /clear or /resume switched sessions in this process
                    s = null;
                }
                if (s == null)
                {
                    s = new Session { Pid = pid, Id = id, Instance = "claude-code:" + id, Entry = Json.Str(json, "entrypoint", "") };
                    live[pid] = s;
                    Sources.Presence(Origin, ClaudeCode.Agent, s.Instance, true, IntPtr.Zero, pid, "Claude Code", false);
                    Log.Write("claude-code session " + Short(id) + " (pid " + pid + ", " + s.Entry + ") found");
                    if (s.Entry == "cli") LookUpHost(s);
                }
                s.FileTime = time;
                UpdateStatus(s, Json.Str(json, "status", ""));
            }
            foreach (var pid in live.Keys.ToList())
            {
                if (seen.Contains(pid) && Proc.Alive(pid)) continue;
                End(live[pid]);
                live.Remove(pid);
            }
            LiveCount = live.Count;
            Sources.Status["claude-code"] = live.Count == 0 ? "No sessions running"
                : live.Count == 1 ? "1 session" : live.Count + " sessions";
        }

        void End(Session s)
        {
            if (s.Tail != null) s.Tail.Dispose();
            Sources.Presence(Origin, ClaudeCode.Agent, s.Instance, false, IntPtr.Zero, s.Pid, "Claude Code", false);
            Log.Write("claude-code session " + Short(s.Id) + " ended");
        }

        // The session file's status flips to "busy" when a turn starts and "idle" when it ends.
        void UpdateStatus(Session s, string status)
        {
            if (status == s.Status) return;
            Log.Write("claude-code session " + Short(s.Id) + " status " + (s.Status.Length == 0 ? "-" : s.Status) + " -> " + status);
            s.Status = status;
            if (status == "busy")
            {
                if (!s.Busy) Send(s, Signal.Think);
                s.Busy = true;
            }
            else if (status == "idle")
            {
                if (s.Busy) Send(s, Signal.Done);
                s.Busy = false;
            }
            else if (status.IndexOf("wait", StringComparison.OrdinalIgnoreCase) >= 0
                  || status.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0
                  || status.IndexOf("input", StringComparison.OrdinalIgnoreCase) >= 0
                  || status.IndexOf("approv", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Send(s, Signal.NeedsApproval);
            }
        }

        void Follow(Session s)
        {
            if (s.Tail == null)
            {
                if (DateTime.UtcNow < s.NextTranscriptLook) return;
                s.NextTranscriptLook = DateTime.UtcNow.AddSeconds(5);
                string path = FindTranscript(s.Id);
                if (path == null) return;
                try { s.Tail = new JsonlTail(path, true); }
                catch (Exception e) { Log.Write("claude-code transcript " + path + ": " + e.Message); }
                return;
            }
            foreach (var line in s.Tail.ReadNew())
            {
                var signals = MapLine(line);
                if (signals.Count > 0) Send(s, signals.ToArray());
            }
        }

        string FindTranscript(string id)
        {
            if (!Directory.Exists(projectsDir)) return null;
            foreach (var dir in Directory.GetDirectories(projectsDir))
            {
                string path = Path.Combine(dir, id + ".jsonl");
                if (File.Exists(path)) return path;
            }
            return null;
        }

        // One transcript entry -> pet signals. Entries are written as the turn happens: the user's prompt,
        // each thinking / text / tool-use block, each tool result.
        List<Signal> MapLine(string line)
        {
            var signals = new List<Signal>();
            if (line.Length > 1024 * 1024) return signals;   // huge tool output or pasted file: nothing to learn
            IDictionary<string, object> o;
            try { o = Json.Parse(line); }
            catch (Exception) { return signals; }
            if (o == null || Json.Bool(o, "isMeta")) return signals;
            string type = Json.Str(o, "type", "");
            object m;
            var message = o.TryGetValue("message", out m) ? m as IDictionary<string, object> : null;

            if (type == "assistant" && message != null)
            {
                foreach (var block in Blocks(message))
                {
                    switch (Json.Str(block, "type", ""))
                    {
                        case "thinking":
                        case "redacted_thinking":
                            signals.Add(Signal.Think);
                            break;
                        case "tool_use":
                            string tool = Json.Str(block, "name", "");
                            signals.Add(ClaudeCode.AskTools.Contains(tool) ? Signal.NeedsApproval : ClaudeCode.ClassifyTool(tool));
                            break;
                    }
                }
                if (Json.Str(message, "stop_reason", "") == "end_turn" && !Json.Bool(o, "isSidechain")) signals.Add(Signal.Done);
            }
            else if (type == "user" && message != null)
            {
                object content;
                message.TryGetValue("content", out content);
                if (content is string)
                {
                    AddPrompt(signals, (string)content);
                    return signals;
                }
                foreach (var block in Blocks(message))
                {
                    string kind = Json.Str(block, "type", "");
                    if (kind == "text") AddPrompt(signals, Json.Str(block, "text", ""));
                    else if (kind == "tool_result")
                    {
                        string text = ResultText(block);
                        if (text.StartsWith("[Request interrupted", StringComparison.Ordinal)) signals.Add(Signal.Cancelled);
                        else if (Json.Bool(block, "is_error")) signals.Add(ClaudeCode.LooksLikeRejection(text) ? Signal.Denied : Signal.Error);
                        else signals.Add(Signal.Think);   // reading the result before the next step
                    }
                }
            }
            else if (type == "system")
            {
                string subtype = Json.Str(o, "subtype", "");
                if (subtype.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 && Json.Str(o, "level", "") == "error")
                    signals.Add(Signal.FatalError);
                else if (subtype.Length > 0 && loggedUnknown.Add(subtype))
                    Log.Write("claude-code transcript: system entry '" + subtype + "' (level " + Json.Str(o, "level", "-") + ")");
            }
            return signals;
        }

        static void AddPrompt(List<Signal> signals, string text)
        {
            string t = text.TrimStart();
            if (t.Length == 0 || t.StartsWith("<", StringComparison.Ordinal)) return;   // slash-command plumbing, reminders
            if (t.StartsWith("[Request interrupted", StringComparison.Ordinal)) { signals.Add(Signal.Cancelled); return; }
            signals.Add(Signal.UserPrompted);
        }

        static IEnumerable<IDictionary<string, object>> Blocks(IDictionary<string, object> message)
        {
            object content;
            if (!message.TryGetValue("content", out content) || !(content is object[])) yield break;
            foreach (var item in (object[])content)
            {
                var block = item as IDictionary<string, object>;
                if (block != null) yield return block;
            }
        }

        static string ResultText(IDictionary<string, object> block)
        {
            object content;
            if (!block.TryGetValue("content", out content)) return "";
            if (content is string) return (string)content;
            var parts = content as object[];
            if (parts == null) return "";
            foreach (var p in parts)
            {
                var d = p as IDictionary<string, object>;
                if (d != null && Json.Str(d, "type", "") == "text") return Json.Str(d, "text", "");
            }
            return "";
        }

        void Send(Session s, params Signal[] signals)
        {
            Sources.Send(Origin, ClaudeCode.Agent, s.Instance, signals);
        }

        // The terminal window hosting a CLI session: a short-lived helper borrows the session's console
        // (attaching to another console from the app itself would tie the app's life to that console).
        void LookUpHost(Session s)
        {
            int pid = s.Pid;
            string instance = s.Instance;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var psi = new ProcessStartInfo(Program.ExePath, "--host-of " + pid)
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                    };
                    using (var p = Process.Start(psi))
                    {
                        if (!p.WaitForExit(5000)) { p.Kill(); return; }
                        if (p.ExitCode == 0) return;
                        var host = new IntPtr(p.ExitCode);
                        Sources.Presence(Origin, ClaudeCode.Agent, instance, true, host, pid, "Claude Code", false);
                        Log.Write("claude-code session pid " + pid + " runs in '" + Native.WindowText(host) + "' [" + Native.WindowClass(host) + "]");
                    }
                }
                catch (Exception e) { Log.Write("host lookup for pid " + pid + " failed: " + e.Message); }
            });
        }

        static string Short(string id) { return id.Length > 8 ? id.Substring(0, 8) : id; }
    }
}
