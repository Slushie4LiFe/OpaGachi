using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpaGachi
{
    // A hover tip: the text and the area of the control it explains (client coordinates).
    sealed class TipRegion
    {
        public readonly string Text;
        public readonly Rectangle Bounds;
        public TipRegion(string text, Rectangle bounds) { Text = text; Bounds = bounds; }
    }

    // Tooltips (Gemini Response 3, 4.1b): shown after a 420 ms hover, straight away when moving between
    // tips within 300 ms; Elevated fill, BorderStrong edge, at most 260 px wide, 6 px padding; fade in over
    // 130 ms rising 4 px, out in 90 ms. ShowNow also opens one on click (the "How to install" help).
    static class Tip
    {
        static TipWindow window;
        static Control host;
        static TipRegion pending, shown;
        static DateTime hiddenAt = DateTime.MinValue;
        static readonly Timer delay = new Timer { Interval = 400 };

        static Tip()
        {
            delay.Tick += (s, e) =>
            {
                delay.Stop();
                if (pending != null && host != null && !host.IsDisposed && host.Visible) ShowNow(host, pending);
            };
        }

        public static void Attach(Control c, Func<string> text)
        {
            Attach(c, p => { string t = text(); return string.IsNullOrEmpty(t) ? null : new TipRegion(t, c.ClientRectangle); });
        }

        public static void Attach(Control c, Func<Point, TipRegion> at)
        {
            c.MouseMove += (s, e) => Hover(c, at(e.Location));
            c.MouseLeave += (s, e) => { if (host == c) Hide(); };
            c.MouseDown += (s, e) => { if (host == c && shown != null && !shown.Text.Contains("\n")) Hide(); };
            c.Disposed += (s, e) => { if (host == c) Hide(); };
        }

        static bool Same(TipRegion a, TipRegion b) { return a != null && b != null && a.Text == b.Text && a.Bounds == b.Bounds; }

        static void Hover(Control c, TipRegion r)
        {
            if (r == null) { if (host == c) Hide(); return; }
            if (host == c && (Same(shown, r) || Same(pending, r))) return;
            bool quick = shown != null || (DateTime.UtcNow - hiddenAt).TotalMilliseconds < 300;
            host = c;
            pending = r;
            delay.Stop();
            if (quick) ShowNow(c, r); else delay.Start();
        }

        public static void ShowNow(Control c, TipRegion r)
        {
            delay.Stop();
            pending = null;
            host = c;
            shown = r;
            if (window == null || window.IsDisposed) window = new TipWindow();
            window.Popup(r.Text, c.RectangleToScreen(r.Bounds));
        }

        public static void Hide()
        {
            delay.Stop();
            pending = null;
            if (shown != null) hiddenAt = DateTime.UtcNow;
            shown = null;
            host = null;
            if (window != null && !window.IsDisposed) window.FadeOut();
        }
    }

    sealed class TipWindow : Form, IAnimated
    {
        string text = "";
        double t;
        bool showing;
        int baseY;

        public TipWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Elevated;
            Font = Theme.Ui(8.25F);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Opacity = 0;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.Left;

        public void Popup(string message, Rectangle anchor)
        {
            text = message;
            var size = TextRenderer.MeasureText(message, Font, new Size(260 - 16, 0), Flags);
            Size = new Size(Math.Min(260, size.Width + 16), size.Height + 12);
            var area = Screen.FromRectangle(anchor).WorkingArea;
            // Centre under narrow anchors (pills, buttons); follow the cursor along wide ones (rows).
            int cx = anchor.Width > 220 ? Cursor.Position.X : anchor.Left + anchor.Width / 2;
            int x = Math.Max(area.Left + 4, Math.Min(area.Right - Width - 4, cx - Width / 2));
            int y = anchor.Bottom + 6;
            if (y + Height > area.Bottom - 4) y = anchor.Top - 6 - Height;
            baseY = y;
            using (var path = Theme.Round(new RectangleF(0, 0, Width, Height), 5)) Region = new Region(path);
            showing = true;
            if (!Visible)
            {
                t = 0;
                Opacity = 0;
                Location = new Point(x, y + (Theme.ReduceMotion ? 0 : 4));
                Show();
            }
            else Location = new Point(x, y);
            Invalidate();
            Animator.Start(this);
        }

        public void FadeOut()
        {
            if (!showing) return;
            showing = false;
            Animator.Start(this);
        }

        public bool Step(double dt)
        {
            if (IsDisposed) return false;
            double ms = Theme.FastMs;
            t = showing ? Math.Min(1, t + dt * 1000 / ms) : Math.Max(0, t - dt * 1000 / ms);
            double k = showing ? Theme.Decel(t) : t;
            Opacity = Math.Max(0, Math.Min(1, k));
            if (showing && !Theme.ReduceMotion) Top = baseY + (int)Math.Round(4 * (1 - k));
            if (!showing && t <= 0) { Hide(); return false; }
            return showing ? t < 1 : true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Elevated);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Dark ? Theme.Mix(Theme.Elevated, Color.White, 0.14) : Theme.BorderStrong)) using (var p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 5)) g.DrawPath(pen, p);
            TextRenderer.DrawText(g, text, Font, new Rectangle(8, 6, Width - 16, Height - 12), Theme.TextPrimary, Flags);
        }
    }
}
