using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpaGachi
{
    // Something an activity source noticed. Sources (Claude Code sessions, Codex threads, desktop apps)
    // run on background threads and only enqueue these; PetManager applies them on the UI thread.
    sealed class SourceEvent
    {
        public string Origin, Agent, Instance, Label;
        public Signal[] Signals = new Signal[0];
        public bool? Present;        // true: the agent's window or session exists; false: it went away
        public bool NeedsWindow;     // the pet belongs to an app window and leaves (once idle) when it closes
        public IntPtr Host;          // the agent's window, for "viewed" detection and hiding
        public int Pid;              // the agent's process, if known
    }

    static class Sources
    {
        public static readonly ConcurrentQueue<SourceEvent> Queue = new ConcurrentQueue<SourceEvent>();

        // One line per source for Settings → Connections ("Watching 2 sessions", "Gemini app open", ...).
        public static readonly ConcurrentDictionary<string, string> Status = new ConcurrentDictionary<string, string>();

        public static void Send(string origin, string agent, string instance, params Signal[] signals)
        {
            if (signals.Length == 0) return;
            Queue.Enqueue(new SourceEvent { Origin = origin, Agent = agent, Instance = instance, Signals = signals });
        }

        public static void Presence(string origin, string agent, string instance, bool present, IntPtr host, int pid, string label, bool needsWindow)
        {
            Queue.Enqueue(new SourceEvent
            {
                Origin = origin, Agent = agent, Instance = instance, Present = present,
                Host = host, Pid = pid, Label = label, NeedsWindow = needsWindow,
            });
        }

        // Profiling: time each source's thread spends working (Stopwatch ticks), logged once a minute.
        public static long ClaudeTicks, CodexTicks, AppTicks;

        public static string Home
        {
            // OPAGACHI_HOME lets tests point every agent folder, the data folder and Factory Reset at fixtures, never the real ones
            get { string dev = Environment.GetEnvironmentVariable("OPAGACHI_HOME"); return string.IsNullOrEmpty(dev) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : dev; }
        }
    }

    // Follows a growing JSON-lines file and returns the complete lines added since the last call.
    // Reads through its own handle (shared read/write/delete), so it sees appends even while the writer
    // keeps the file open, and never blocks the writer from rotating or deleting it.
    sealed class JsonlTail : IDisposable
    {
        const int MaxLine = 4 * 1024 * 1024;     // longer lines (pasted files, huge tool output) are skipped
        const int MaxCatchUp = 8 * 1024 * 1024;  // if we fall further behind than this, jump ahead

        public readonly string Path;
        FileStream stream;
        long position;
        readonly MemoryStream partial = new MemoryStream();
        bool skipping;   // inside an over-long line: drop bytes until its newline
        byte[] buffer = new byte[64 * 1024];
        public DateTime LastGrowth = DateTime.UtcNow;

        public JsonlTail(string path, bool fromEnd)
        {
            Path = path;
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            position = fromEnd ? stream.Length : 0;   // writers append whole lines, so the end is a line boundary
        }

        public List<string> ReadNew()
        {
            var lines = new List<string>();
            if (stream == null) return lines;
            long length;
            try { length = stream.Length; }
            catch (Exception) { return lines; }
            if (length < position)
            {
                position = 0;   // truncated or replaced: start over
                partial.SetLength(0);
                skipping = false;
            }
            if (length == position) return lines;
            if (length - position > MaxCatchUp)
            {
                position = length - MaxCatchUp;
                partial.SetLength(0);
                skipping = true;   // we landed mid-line
            }
            LastGrowth = DateTime.UtcNow;
            stream.Position = position;
            while (position < length)
            {
                int want = (int)Math.Min(buffer.Length, length - position);
                int n = stream.Read(buffer, 0, want);
                if (n <= 0) break;
                position += n;
                int start = 0;
                for (int i = 0; i < n; i++)
                {
                    if (buffer[i] != (byte)'\n') continue;
                    if (!skipping)
                    {
                        partial.Write(buffer, start, i - start);
                        if (partial.Length > 0) lines.Add(Encoding.UTF8.GetString(partial.GetBuffer(), 0, (int)partial.Length).TrimEnd('\r'));
                    }
                    partial.SetLength(0);
                    skipping = false;
                    start = i + 1;
                }
                if (!skipping && start < n)
                {
                    partial.Write(buffer, start, n - start);
                    if (partial.Length > MaxLine) { partial.SetLength(0); skipping = true; }
                }
            }
            return lines;
        }

        public void Dispose()
        {
            if (stream != null) stream.Dispose();
            stream = null;
        }
    }

    // Process lookups that are cheap enough to run every second.
    static class Proc
    {
        static readonly Dictionary<uint, KeyValuePair<string, DateTime>> names = new Dictionary<uint, KeyValuePair<string, DateTime>>();
        static readonly object Gate = new object();

        // Full path of a process's executable ("" if it can't be read). Cached for a minute per pid.
        public static string ImagePath(uint pid)
        {
            lock (Gate)
            {
                KeyValuePair<string, DateTime> hit;
                if (names.TryGetValue(pid, out hit) && (DateTime.UtcNow - hit.Value).TotalSeconds < 60) return hit.Key;
                string path = "";
                IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new StringBuilder(1024);
                        int size = sb.Capacity;
                        if (Native.QueryFullProcessImageName(h, 0, sb, ref size)) path = sb.ToString();
                    }
                    finally { Native.CloseHandle(h); }
                }
                if (names.Count > 2000) names.Clear();
                names[pid] = new KeyValuePair<string, DateTime>(path, DateTime.UtcNow);
                return path;
            }
        }

        public static string ImageName(uint pid)
        {
            string path = ImagePath(pid);
            return path.Length == 0 ? "" : System.IO.Path.GetFileName(path);
        }

        public static bool Alive(int pid)
        {
            if (pid <= 0) return false;
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                uint code;
                return Native.GetExitCodeProcess(h, out code) && code == Native.STILL_ACTIVE;
            }
            finally { Native.CloseHandle(h); }
        }
    }
}
