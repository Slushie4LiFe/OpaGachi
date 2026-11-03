using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace OpaGachi
{
    enum Anchor { BottomLeft, BottomCenter, BottomRight, TopLeft, TopCenter, TopRight }

    // Where pets may appear.
    enum Visibility
    {
        DesktopOnly,      // at desktop level: every app window covers them
        AllButAiApps,     // on top of everything, hidden while any AI app (or agent terminal) is in front
        AllButOwnApp,     // on top of everything, each pet hidden while its own AI is in front
    }

    sealed class AgentInfo
    {
        public string Key, Name;
        public Color? Tie;
        public string Texture;                    // optional tiled tie pattern, relative to the app folder
        public string SparkleTrigger = "Both", SparkleColor;
        public string NameColor;                  // name tag fill: "#RRGGBB" or a NameFill preset; null = the agent's own preset
        public bool Enabled = true;               // off = no pet for this agent
        public bool Hidden;                       // internal agents (the demo) don't appear in Settings
        public string[] Apps = new string[0];     // process names of the agent's desktop app ("ChatGPT.exe")
    }

    // settings.json in the app folder. Missing keys keep their defaults; unknown keys are ignored.
    sealed class Settings
    {
        public Anchor Anchor = Anchor.BottomRight;
        public int Port = 47321;
        public double BoredAfterSeconds = 600;
        public double OccasionalEverySeconds = 8;
        public double OccasionalChance = 0.2;
        public double Spacing = 0.7;          // horizontal step between pets, as a fraction of sprite width
        public int Margin = 16;               // distance from the screen edge, in pixels
        public bool Badges = true;            // status bubble next to the pet's head
        public bool ShowWelcome = true;       // first-run window
        public bool HookReminder = true;      // offer missing hooks at start ("Don't show this again" clears it)
        public int Size = 2;                  // index into SizeFactors: XS, S, Normal, L, XL
        public const double StandingBoost = 0.15;   // extra scale for full-body standing clips; fixed, no longer a setting (user, 2026-11-01)
        public Visibility Visibility = Visibility.AllButOwnApp;
        public bool ClickPets = true;         // click a pet to bring its app forward (only the figure catches clicks)
        public bool HoverInfo = true;         // hovering a pet shows what it's doing in its name tag
        public bool? ReduceMotionChoice;      // null = follow Windows' "Animation effects" switch
        public bool Dark;                     // appearance: false = Light (the default), true = Dark
        public bool HighContrast;             // accessibility: solid surfaces, strong text and edges
        public int TextSize = 1;              // index into TextScales: Small, Default, Large, Larger
        public bool Sound;                    // play a sound when an agent finishes (off until switched on)
        public string SoundFile = "Comet.mp3";   // a name in assets/sounds, or a full path to the user's own file
        public bool CustomSprites;            // experimental: every agent uses one generic sprite (assets/sprites/custom)
        public string Display = "";           // the display pets live on (a device name like \.\DISPLAY1); "" = the main display
        public bool CustomPlacement;          // experimental: the row stands where the user dragged it
        public double CustomX = 0.5, CustomY = 1;   // that spot: the row's centre and ground line, as fractions of the display's work area

        public static readonly string[] TextSizeNames = { "Small", "Default", "Large", "Larger" };
        public static readonly float[] TextScales = { 0.9f, 1f, 1.15f, 1.3f };
        public float TextScale { get { return TextScales[Math.Max(0, Math.Min(TextScales.Length - 1, TextSize))]; } }

        public bool ReduceMotion { get { return ReduceMotionChoice.HasValue ? ReduceMotionChoice.Value : !Native.AnimationsOn(); } }

        public static readonly string[] SizeNames = { "XS", "S", "Normal", "L", "XL" };
        public static readonly double[] SizeFactors = { 0.6, 0.8, 1.0, 1.2, 1.4 };
        public double SizeFactor { get { return SizeFactors[Math.Max(0, Math.Min(SizeFactors.Length - 1, Size))]; } }

        string path;

        public static Settings Load(string path)
        {
            var s = new Settings { path = path };
            var d = Json.ReadFile(path);
            if (d == null) return s;
            Anchor anchor;
            if (Enum.TryParse(Json.Str(d, "anchor", ""), true, out anchor)) s.Anchor = anchor;
            s.Port = (int)Json.Num(d, "port", s.Port);
            s.BoredAfterSeconds = Json.Num(d, "boredAfterSeconds", s.BoredAfterSeconds);
            s.OccasionalEverySeconds = Json.Num(d, "occasionalEverySeconds", s.OccasionalEverySeconds);
            s.OccasionalChance = Json.Num(d, "occasionalChance", s.OccasionalChance);
            s.Spacing = Json.Num(d, "spacing", s.Spacing);
            s.Margin = (int)Json.Num(d, "margin", s.Margin);
            s.Badges = Json.BoolOr(d, "badges", s.Badges);
            s.ShowWelcome = Json.BoolOr(d, "showWelcome", s.ShowWelcome);
            s.HookReminder = Json.BoolOr(d, "hookReminder", s.HookReminder);
            s.Size = Math.Max(0, Math.Min(SizeFactors.Length - 1, (int)Json.Num(d, "size", s.Size)));
            Visibility visibility;
            if (Enum.TryParse(Json.Str(d, "visibility", ""), true, out visibility)) s.Visibility = visibility;
            s.ClickPets = Json.BoolOr(d, "clickPets", s.ClickPets);
            s.HoverInfo = Json.BoolOr(d, "hoverInfo", s.HoverInfo);
            object motion;
            if (d.TryGetValue("reduceMotion", out motion) && motion is bool) s.ReduceMotionChoice = (bool)motion;
            s.Dark = Json.Str(d, "appearance", "light") == "dark";
            s.HighContrast = Json.BoolOr(d, "highContrast", false);
            s.TextSize = Math.Max(0, Math.Min(TextScales.Length - 1, (int)Json.Num(d, "textSize", s.TextSize)));
            s.Sound = Json.BoolOr(d, "sound", s.Sound);
            s.SoundFile = Json.Str(d, "soundFile", s.SoundFile);
            s.CustomSprites = Json.BoolOr(d, "customSprites", false);
            s.Display = Json.Str(d, "display", "");
            s.CustomPlacement = Json.BoolOr(d, "customPlacement", false);
            s.CustomX = Math.Max(0, Math.Min(1, Json.Num(d, "customX", s.CustomX)));
            s.CustomY = Math.Max(0, Math.Min(1, Json.Num(d, "customY", s.CustomY)));
            return s;
        }

        public void Save()
        {
            var d = new Dictionary<string, object>();
            d["anchor"] = Anchor.ToString();
            d["port"] = Port;
            d["boredAfterSeconds"] = BoredAfterSeconds;
            d["occasionalEverySeconds"] = OccasionalEverySeconds;
            d["occasionalChance"] = OccasionalChance;
            d["spacing"] = Spacing;
            d["margin"] = Margin;
            d["badges"] = Badges;
            d["showWelcome"] = ShowWelcome;
            d["hookReminder"] = HookReminder;
            d["size"] = Size;
            d["visibility"] = Visibility.ToString();
            d["clickPets"] = ClickPets;
            d["hoverInfo"] = HoverInfo;
            if (ReduceMotionChoice.HasValue) d["reduceMotion"] = ReduceMotionChoice.Value;
            d["appearance"] = Dark ? "dark" : "light";
            d["highContrast"] = HighContrast;
            d["textSize"] = TextSize;
            d["sound"] = Sound;
            d["soundFile"] = SoundFile;
            d["customSprites"] = CustomSprites;
            d["display"] = Display;
            d["customPlacement"] = CustomPlacement;
            d["customX"] = CustomX;
            d["customY"] = CustomY;
            Json.WriteFile(path, d);
        }

        // assets/agents.json: { "claude": { "name": "Claude", "tie": "#D97757" }, ... }
        public static Dictionary<string, AgentInfo> LoadAgents(string path)
        {
            var agents = new Dictionary<string, AgentInfo>(StringComparer.OrdinalIgnoreCase);
            var d = Json.ReadFile(path);
            if (d == null) return agents;
            foreach (var kv in d)
            {
                var entry = kv.Value as IDictionary<string, object>;
                if (entry == null) continue;
                var info = new AgentInfo
                {
                    Key = kv.Key,
                    Name = Json.Str(entry, "name", kv.Key),
                    Texture = Json.Str(entry, "texture", null),
                    Enabled = Json.BoolOr(entry, "enabled", true),
                    Hidden = Json.Bool(entry, "hidden"),
                    Apps = Json.Strings(entry, "apps"),
                };
                info.Tie = ColorOf(entry, "tie", kv.Key);
                info.NameColor = Json.Str(entry, "nameColor", null);
                info.SparkleTrigger = Json.Str(entry, "sparkleTrigger", "Both");
                info.SparkleColor = Json.Str(entry, "sparkleColor", null);
                agents[kv.Key] = info;
            }
            return agents;
        }

        static Color? ColorOf(IDictionary<string, object> entry, string key, string agent)
        {
            string hex = Json.Str(entry, key, null);
            if (hex == null) return null;
            try { return ColorTranslator.FromHtml(hex); }
            catch (Exception) { Log.Write("agents.json: bad " + key + " '" + hex + "' for " + agent); return null; }
        }

        public static string HexOf(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B); }

        public static void SaveAgents(string path, Dictionary<string, AgentInfo> agents)
        {
            var root = new Dictionary<string, object>();
            foreach (var info in agents.Values)
            {
                var d = new Dictionary<string, object>();
                d["name"] = info.Name;
                if (info.Tie.HasValue) d["tie"] = HexOf(info.Tie.Value);
                if (!string.IsNullOrEmpty(info.Texture)) d["texture"] = info.Texture;
                if (!string.IsNullOrEmpty(info.NameColor)) d["nameColor"] = info.NameColor;
                d["sparkleTrigger"] = info.SparkleTrigger;
                if (info.SparkleColor != null) d["sparkleColor"] = info.SparkleColor;
                d["enabled"] = info.Enabled;
                if (info.Hidden) d["hidden"] = true;
                if (info.Apps.Length > 0) d["apps"] = info.Apps;
                root[info.Key] = d;
            }
            Json.WriteFile(path, root);
        }
    }

    // Name tag fills (user, 2026-11-02): a solid colour or one of four gradient presets, one per agent by default:
    // Claude orange, ChatGPT/Codex astral purple, Gemini pastel pearl on white, Grok black to silver. Stops run
    // top to bottom; the dark outline keeps any of them readable on any wallpaper.
    static class NameFill
    {
        public static readonly string[] Presets = { "orange", "astral", "pearl", "silver" };
        public const string White = "#F4F4F6";

        public static string Label(string style)
        {
            switch (style)
            {
                case "orange": return "Orange";
                case "astral": return "Astral purple";
                case "pearl": return "Pearl";
                case "silver": return "Silver";
            }
            return style == White ? "White" : "Custom colour";
        }

        public static string DefaultFor(string agent)
        {
            switch (agent)
            {
                case "claude": return "orange";
                case "chatgpt": return "astral";
                case "gemini": return "pearl";
                case "grok": return "silver";
                default: return White;
            }
        }

        public static string Of(AgentInfo info, string agent) { return info != null && !string.IsNullOrEmpty(info.NameColor) ? info.NameColor : DefaultFor(agent); }

        public static Color[] Stops(string style)
        {
            switch (style)
            {
                case "orange": return new[] { Theme.Hex(0xFFD2A1), Theme.Hex(0xFF9A52), Theme.Hex(0xE0612B) };
                case "astral": return new[] { Theme.Hex(0xE2CCFF), Theme.Hex(0xA77BFF), Theme.Hex(0x6A38D6) };
                case "pearl": return new[] { Theme.Hex(0xFFFFFF), Theme.Hex(0xFFC9E8), Theme.Hex(0xD5C6FF), Theme.Hex(0xB8EEF2), Theme.Hex(0xFFF1CC) };   // iridescent: pink, lilac, aqua, cream on white (user, 2026-11-02)
                case "silver": return new[] { Theme.Hex(0xF4F5F7), Theme.Hex(0xA9AEB6), Theme.Hex(0x3C3F45) };
            }
            try { return new[] { ColorTranslator.FromHtml(style) }; }
            catch (Exception) { return new[] { Theme.Hex(0xF4F4F6) }; }
        }

        // A brush over `bounds` (the text's own bounds, so the whole gradient shows on every name).
        public static Brush Brush(string style, RectangleF bounds)
        {
            var stops = Stops(style);
            if (stops.Length == 1 || bounds.Height < 1) return new SolidBrush(stops[0]);
            var b = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(bounds.X, bounds.Y - 0.5f, Math.Max(1, bounds.Width), bounds.Height + 1), stops[0], stops[stops.Length - 1], 90f);
            b.InterpolationColors = new System.Drawing.Drawing2D.ColorBlend { Colors = stops, Positions = Enumerable.Range(0, stops.Length).Select(i => i / (float)(stops.Length - 1)).ToArray() };
            return b;
        }
    }

    // Small JSON helpers over JavaScriptSerializer, plus an indented writer
    // (used for settings files, including the user's Claude Code settings.json).
    static class Json
    {
        // One serializer per thread: the activity sources parse on background threads.
        [ThreadStatic] static JavaScriptSerializer serializer;
        static JavaScriptSerializer Serializer
        {
            get { return serializer ?? (serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }); }
        }

        public static IDictionary<string, object> Parse(string text)
        {
            return Serializer.DeserializeObject(text) as IDictionary<string, object>;
        }

        public static IDictionary<string, object> ReadFile(string path)
        {
            if (!File.Exists(path)) return null;
            try { return Parse(File.ReadAllText(path)); }
            catch (Exception e)
            {
                Log.Write("Could not read " + path + ": " + e.Message);
                return null;
            }
        }

        public static void WriteFile(string path, object value)
        {
            File.WriteAllText(path, Write(value) + "\n", new UTF8Encoding(false));
        }

        public static string Str(IDictionary<string, object> d, string key, string fallback)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is string ? (string)v : fallback;
        }

        public static double Num(IDictionary<string, object> d, string key, double fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null || v is string || v is bool) return fallback;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        public static string[] Strings(IDictionary<string, object> d, string key)
        {
            object v;
            var list = new List<string>();
            if (d != null && d.TryGetValue(key, out v) && v is object[])
                foreach (var item in (object[])v) if (item is string) list.Add((string)item);
            return list.ToArray();
        }

        public static bool Bool(IDictionary<string, object> d, string key)
        {
            return BoolOr(d, key, false);
        }

        public static bool BoolOr(IDictionary<string, object> d, string key, bool fallback)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is bool ? (bool)v : fallback;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object value, int depth)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is string) { Quote(sb, (string)value); return; }
            if (value is bool) { sb.Append((bool)value ? "true" : "false"); return; }
            if (value is double || value is float || value is decimal || value is int || value is long || value is short || value is uint || value is ulong)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }
            string pad = new string(' ', (depth + 1) * 2), end = new string(' ', depth * 2);
            var dict = value as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (var kv in dict)
                {
                    sb.Append(pad);
                    Quote(sb, kv.Key);
                    sb.Append(": ");
                    Write(sb, kv.Value, depth + 1);
                    sb.Append(++i < dict.Count ? ",\n" : "\n");
                }
                sb.Append(end).Append('}');
                return;
            }
            var list = value as IEnumerable;
            if (list != null)
            {
                var items = new List<object>();
                foreach (var item in list) items.Add(item);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(pad);
                    Write(sb, items[i], depth + 1);
                    sb.Append(i + 1 < items.Count ? ",\n" : "\n");
                }
                sb.Append(end).Append(']');
                return;
            }
            Quote(sb, value.ToString());
        }

        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    // Append-only log next to settings.json; trimmed at startup when it grows past 512 KB.
    static class Log
    {
        static string path;
        static readonly object Gate = new object();

        public static void Init(string file)
        {
            path = file;
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            }
            catch (Exception) { }
        }

        public static void Write(string message)
        {
            if (path == null) return;
            lock (Gate)
            {
                try { File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message + Environment.NewLine); }
                catch (Exception) { }
            }
        }
    }
}
