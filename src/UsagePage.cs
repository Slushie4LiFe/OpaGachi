using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace OpaGachi
{
    // Settings → Usage, drawn as one see-through panel. Claude and ChatGPT/Codex look the same (user, 2026-10-31):
    // one glass card each (user, 2026-11-02: not two), with the 5 hr and 168 hr limits as 270° rings that sweep in
    // and count up (the % inside, the reset time under it) and, under a hairline, the tokens: today's total with
    // the 30-day sum beside a heat map of the 30 days. Claude without the status line relay shows where to add it
    // in place of the rings. Grok and Gemini (user, 2026-11-02: a card for all four) have no readable limits, so
    // their cards say where the limits are instead: Grok shows tokens and estimated cost from its session records,
    // Gemini the answers OpaGachi counted.
    sealed class UsagePanel : Control, IAnimated
    {
        sealed class Gauge
        {
            public string Title, WaitingText;
            public double Percent = -1;            // -1: no data yet
            public DateTime ResetsAt = DateTime.MinValue, Seen = DateTime.MinValue;
            public int WindowMinutes;
            public double From, To, Start = -1;     // tween of the shown percentage
            public int Ms;
            public Rectangle Bounds;
        }

        // One agent's 30 days: the card's two gauges and the heat map.
        sealed class Section
        {
            public string Title;
            public Gauge Five, Week;                       // null: no readable plan limits
            public string BannerTitle, BannerText;         // shown in place of the rings
            public string Measure = "Tokens today", Unit = "tokens", Extra = "";
            public long[] Days = new long[UsageHistory.Days];
            public int Top, End;
            public Rectangle Card, Tokens;
            public Rectangle[] Cells = new Rectangle[UsageHistory.Days];
        }

        // The heat map (user, 2026-11-01): 3 rows of 10 days read like text, oldest top left, today bottom right.
        // Cell i is days[i] of the 30-day history (oldest first, today last).
        const int HeatCols = 10;
        static int HeatRows { get { return (UsageHistory.Days + HeatCols - 1) / HeatCols; } }
        static DateTime DayOf(int i) { return DateTime.Today.AddDays(i - (UsageHistory.Days - 1)); }

        readonly Section claude = new Section
        {
            Title = "Claude",
            Five = new Gauge { Title = "5 hr" },
            Week = new Gauge { Title = "168 hr" },
            BannerTitle = "Show 5 hr and 168 hr plan limits",
            BannerText = "Claude Code reports plan limits only through its status line. Add the Claude plan limits relay on the Connections page; your status line stays as it is.",
        };
        readonly Section codex = new Section
        {
            Title = "ChatGPT and Codex",
            Five = new Gauge { Title = "5 hr", WaitingText = "No Codex activity yet" },
            Week = new Gauge { Title = "168 hr", WaitingText = "No Codex activity yet" },
        };
        readonly Section grok = new Section
        {
            Title = "Grok",
            BannerTitle = "Plan limits stay in Grok",
            BannerText = "Grok doesn't share its plan limits with other apps; open /usage in Grok to see them. Tokens and cost below come from Grok's own session records.",
        };
        readonly Section gemini = new Section
        {
            Title = "Gemini",
            Measure = "Answers today",
            Unit = "answers",
            BannerTitle = "Gemini keeps no usage on this PC",
            BannerText = "The Gemini app stores no token counts or plan limits that other apps can read, so OpaGachi counts the answers it sees instead.",
        };
        Section[] Sections { get { return new[] { claude, codex, grok, gemini }; } }
        Gauge[] Gauges { get { return new[] { claude.Five, claude.Week, codex.Five, codex.Week }; } }

        bool relay;
        readonly Stopwatch clock = Stopwatch.StartNew();

        readonly Font heading = Theme.Semibold(9F), ui = Theme.Ui(9F), small = Theme.Ui(8.25F),
            percentFont = Theme.Semibold(14F), bigFont = Theme.Semibold(18F);

        public UsagePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 600;
            relay = Integrations.Relay;
            Tip.Attach(this, TipAt);
        }

        double Now { get { return clock.Elapsed.TotalMilliseconds; } }

        // Read the latest numbers; changed values glide to their new place.
        public void UpdateFrom(PetManager pets)
        {
            bool relayNow = Integrations.Relay;
            if (relayNow != relay) { relay = relayNow; PerformLayout(); }
            claude.Five.WaitingText = claude.Week.WaitingText = "Waiting for Claude Code";
            bool hasClaude = ClaudeUsage.LimitsSeen != DateTime.MinValue;
            Set(claude.Five, hasClaude ? ClaudeUsage.FiveHour : null, ClaudeUsage.LimitsSeen);
            Set(claude.Week, hasClaude ? ClaudeUsage.Weekly : null, ClaudeUsage.LimitsSeen);
            bool hasCodex = CodexUsage.LimitsSeen != DateTime.MinValue;
            Set(codex.Five, hasCodex ? CodexUsage.FiveHour : null, CodexUsage.LimitsSeen);
            Set(codex.Week, hasCodex ? CodexUsage.Weekly : null, CodexUsage.LimitsSeen);
            claude.Days = UsageHistory.Claude;
            codex.Days = UsageHistory.Codex;
            grok.Days = UsageHistory.Grok;
            long cost = UsageHistory.GrokCost.Sum();
            grok.Extra = cost > 0 ? " · est. " + UsageText.Dollars(cost) : "";
            gemini.Days = AnswerLog.Series("gemini");
            Invalidate();
            Animator.Start(this);
        }

        void Set(Gauge g, LimitWindow w, DateTime seen)
        {
            double p = w == null || w.UsedPercent < 0 ? -1 : w.UsedPercent;
            g.Seen = seen;
            g.ResetsAt = w == null ? DateTime.MinValue : w.ResetsAt;
            g.WindowMinutes = w == null ? 0 : w.WindowMinutes;
            if (p == g.Percent) return;
            double shown = Shown(g);
            g.Percent = p;
            Tween(g, shown, Math.Max(0, Effective(g)), 0);
        }

        // Opening the page: every gauge sweeps from zero (Gentle spring, 50 ms apart) after `delayMs`.
        public void Replay(int delayMs)
        {
            var all = Gauges;
            for (int i = 0; i < all.Length; i++) Tween(all[i], 0, Math.Max(0, Effective(all[i])), delayMs + i * 50);
            Invalidate();
            Animator.Start(this);
        }

        // Sweeps follow the Gentle spring's curve (Gemini Response 5); a new value starts from what's shown.
        void Tween(Gauge g, double from, double to, int delay)
        {
            g.From = from; g.To = to; g.Ms = (int)(Feel.Gentle.Seconds * 1000);
            g.Start = Theme.ReduceMotion ? -1 : Now + delay;
        }

        // Percentage the ring should show: 0 once the window has rolled over since the last report.
        static double Effective(Gauge g)
        {
            if (g.Percent < 0) return -1;
            return g.ResetsAt != DateTime.MinValue && g.ResetsAt < DateTime.Now ? 0 : g.Percent;
        }

        double Shown(Gauge g)
        {
            if (g.Start < 0) return Math.Max(0, Effective(g));
            double t = (Now - g.Start) / g.Ms;
            if (t <= 0) return g.From;
            if (t >= 1) return g.To;
            return g.From + (g.To - g.From) * Feel.Gentle.Progress((Now - g.Start) / 1000);
        }

        public bool Step(double dt)
        {
            if (IsDisposed || !Visible) return false;
            double now = Now;
            bool moving = Gauges.Any(g => g.Start >= 0 && now < g.Start + g.Ms);
            bool shimmer = !Theme.ReduceMotion && Gauges.Any(g => g.Percent < 0 && g.Bounds.Width > 0);
            Invalidate();
            return moving || shimmer;
        }

        // ---- Layout ------------------------------------------------------------------------------------

        const int L = GlassCard.Side;   // cards line up with the other pages' cards
        const int CardH = 150, TokensH = 96, BannerH = 92, Cell = 15, Pitch = 18;
        int R { get { return Width - GlassCard.Side; } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int y = 4;
            foreach (var s in Sections)
            {
                s.Top = y;
                y += 32;
                if (s.Five == null || (s == claude && !relay))
                {
                    if (s.Five != null) s.Five.Bounds = s.Week.Bounds = Rectangle.Empty;
                    s.Card = new Rectangle(L, y, R - L, BannerH);
                    y += BannerH;
                }
                else
                {
                    s.Card = new Rectangle(L, y, R - L, CardH);
                    int half = s.Card.Width / 2;
                    s.Five.Bounds = new Rectangle(L, y, half, CardH);
                    s.Week.Bounds = new Rectangle(L + half, y, s.Card.Width - half, CardH);
                    y += CardH;
                }
                s.Tokens = new Rectangle(L, y, R - L, TokensH);
                y += TokensH;
                s.End = y;
                y += 8;
            }
            int h = gemini.End + 20;
            if (Height != h) Height = h;
        }

        // ---- Paint ---------------------------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var s in Sections)
            {
                TextRenderer.DrawText(g, s.Title, heading, new Point(L + 14, s.Top + 12), Theme.TextSecondary, TextFormatFlags.NoPrefix);
                Theme.Glass(g, Rectangle.Union(s.Card, s.Tokens), Theme.RadiusCard);
                using (var hair = new Pen(Theme.Border)) g.DrawLine(hair, s.Card.X + 16, s.Card.Bottom, s.Card.Right - 16, s.Card.Bottom);
                if (s.Five != null && s.Five.Bounds.Width > 0)
                {
                    int mid = s.Card.X + s.Card.Width / 2;
                    using (var hair = new Pen(Theme.Border)) g.DrawLine(hair, mid, s.Card.Y + 20, mid, s.Card.Bottom - 20);
                    DrawGauge(g, s.Five);
                    DrawGauge(g, s.Week);
                }
                else DrawBanner(g, s);
                DrawTokens(g, s);
            }
        }

        static bool Stale(Gauge d) { return Effective(d) > 0.5 && (DateTime.Now - d.Seen).TotalHours > 2; }

        // Every ring uses the same fullness colours (user, 2026-10-31): green, yellow from 50%, orange from 75%,
        // red from 90%. Fixed 2026-11-01: an empty limit shows an empty track (no stray dot) and a small one a
        // short, clean arc (no white stripe over it).
        void DrawGauge(Graphics g, Gauge d)
        {
            var r = d.Bounds;
            double shown = Shown(d);
            bool waiting = d.Percent < 0, stale = !waiting && Stale(d);
            bool danger = !waiting && !stale && Effective(d) >= 90;

            float cx = r.X + r.Width / 2f;
            TextRenderer.DrawText(g, d.Title, ui, new Rectangle(r.X, r.Y + 12, r.Width, 18), Theme.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);

            const float size = 80, stroke = 8;
            float cy = r.Y + 74;
            var ring = new RectangleF(cx - size / 2 + stroke / 2, cy - size / 2 + stroke / 2, size - stroke, size - stroke);
            using (var track = new Pen(Theme.GaugeTrack, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(track, ring, 135, 270);
            if (waiting)
            {
                if (!Theme.ReduceMotion)
                {
                    float spin = (float)(Now / 1400.0 % 1.0 * 270);
                    using (var shimmer = new Pen(Theme.Lift(Theme.GaugeTrack, 0.08), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(shimmer, ring, 135 + Math.Min(spin, 225), 45);
                }
            }
            else if (shown >= 0.5)
            {
                Color arc = stale ? Theme.StaleArc : UsageText.LevelColor(shown);
                float sweep = Math.Max(6, (float)(270 * Math.Min(100, shown) / 100));
                if (danger)
                    using (var glow = new Pen(Color.FromArgb(50, Theme.Danger), stroke + 6) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(glow, ring, 135, sweep);
                using (var pen = new Pen(arc, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pen, ring, 135, sweep);
            }
            string pct = waiting ? "—" : Math.Round(shown) + "%";
            TextRenderer.DrawText(g, pct, percentFont, new Rectangle((int)(cx - 44), (int)(cy - 14), 88, 28), waiting ? Theme.TextMuted : stale ? Theme.TextSecondary : Theme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // Only when it resets; the percentage is already in the ring.
            DateTime next = NextReset(d);
            string caption = waiting ? d.WaitingText : stale ? "Stale · " + UsageText.Ago(d.Seen) : next != DateTime.MinValue ? UsageText.ResetsShort(next) : "";
            TextRenderer.DrawText(g, caption, small, new Rectangle(r.X + 8, r.Y + 122, r.Width - 16, 16), Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        static DateTime NextReset(Gauge d)
        {
            if (d.ResetsAt == DateTime.MinValue) return DateTime.MinValue;
            if (d.ResetsAt > DateTime.Now) return d.ResetsAt;
            if (d.WindowMinutes <= 0) return DateTime.MinValue;
            var next = d.ResetsAt;
            while (next < DateTime.Now) next = next.AddMinutes(d.WindowMinutes);
            return next;
        }

        // No rings: where the limits are instead (Claude: the relay under Connections, user 2026-11-01).
        void DrawBanner(Graphics g, Section s)
        {
            var card = s.Card;
            float ix = card.X + 22, iy = card.Y + 24;
            using (var pen = new Pen(Theme.TextMuted, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, ix, iy, 44, 44, 135, 270);
                g.DrawArc(pen, ix + 11, iy + 11, 22, 22, 135, 200);
            }
            int tx = card.X + 84, tw = card.Right - 20 - tx;
            TextRenderer.DrawText(g, s.BannerTitle, ui, new Point(tx, card.Y + 18), Theme.TextPrimary);
            TextRenderer.DrawText(g, s.BannerText, small, new Rectangle(tx, card.Y + 40, tw, 44), Theme.TextSecondary, TextFormatFlags.WordBreak);
        }

        // Tokens (user, 2026-11-01: less empty space): today's total large, the 30-day sum under it, and the
        // heat map of those 30 days on the right. Cells are shaded in four accent steps relative to the busiest day.
        void DrawTokens(Graphics g, Section s)
        {
            var card = s.Tokens;
            long today = s.Days[s.Days.Length - 1], total = s.Days.Sum(), max = s.Days.Max();
            int x = card.X + 20;
            TextRenderer.DrawText(g, s.Measure, ui, new Point(x, card.Y + 14), Theme.TextSecondary, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, UsageText.Tokens(today), bigFont, new Point(x - 1, card.Y + 32), Theme.TextPrimary, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "30 days · " + UsageText.Tokens(total) + s.Extra, small, new Point(x, card.Y + 68), Theme.TextSecondary, TextFormatFlags.NoPadding);

            int gridW = HeatCols * Pitch - (Pitch - Cell), gridH = HeatRows * Pitch - (Pitch - Cell);
            int gx = card.Right - 20 - gridW, gy = card.Y + (card.Height - gridH) / 2;
            int lead = HeatRows * HeatCols - s.Days.Length;   // empty slots at the top left if the history isn't a multiple of 10
            for (int i = 0; i < s.Cells.Length; i++)
            {
                int slot = i + lead;
                var cell = new Rectangle(gx + slot % HeatCols * Pitch, gy + slot / HeatCols * Pitch, Cell, Cell);
                s.Cells[i] = cell;
                DrawCell(g, cell, Level(s.Days[i], max), i == s.Cells.Length - 1);
            }
        }

        static int Level(long v, long max)
        {
            if (v <= 0 || max <= 0) return 0;
            return 1 + (int)Math.Min(3, 4.0 * v / max);
        }

        static readonly double[] Shade = { 0, 0.3, 0.5, 0.75, 1 };

        // Black outlines on Light, white on Dark (user, 2026-11-01); today's is twice as thick.
        static void DrawCell(Graphics g, Rectangle cell, int level, bool today)
        {
            var r = new RectangleF(cell.X + 0.5f, cell.Y + 0.5f, cell.Width - 1, cell.Height - 1);
            Color fill = level == 0 ? Theme.TrackBottom : Theme.Mix(Theme.Surface, Theme.Accent, Shade[level]);
            using (var b = new SolidBrush(fill)) using (var p = Theme.Round(r, 4)) g.FillPath(b, p);
            if (today) r = RectangleF.Inflate(r, -0.5f, -0.5f);
            using (var pen = new Pen(Theme.Dark ? Color.White : Color.Black, today ? 2f : 1f)) using (var p = Theme.Round(r, 4)) g.DrawPath(pen, p);
        }

        TipRegion TipAt(Point p)
        {
            foreach (var s in Sections)
                for (int i = 0; i < s.Cells.Length; i++)
                    if (s.Cells[i].Contains(p))
                        return new TipRegion((i == s.Cells.Length - 1 ? "Today · " : DayOf(i).ToString("dddd d MMMM", CultureInfo.CurrentCulture) + " · ")
                            + UsageText.Tokens(s.Days[i]) + " " + s.Unit, s.Cells[i]);
            foreach (var d in Gauges)
                if (d.Bounds.Contains(p) && d.Percent >= 0)
                    return new TipRegion(d.Title + ": " + Math.Round(Effective(d)) + "% used" + (d.ResetsAt != DateTime.MinValue ? ", " + UsageText.ResetsShort(NextReset(d)) : "") + "\nLast report " + UsageText.Ago(d.Seen), d.Bounds);
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { heading.Dispose(); ui.Dispose(); small.Dispose(); percentFont.Dispose(); bigFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
