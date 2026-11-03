using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OpaGachi
{
    // First-run window (Gemini Response 2, 2.4): the pet, a one-line pitch, every AI agent found on this
    // PC with how it's connected (and a one-click Connect where a hook helps), the demo, Get started.
    sealed class WelcomeForm : GlassForm
    {
        readonly Settings settings;
        readonly PetManager pets;
        readonly CheckRow dontShow = new CheckRow("Don't show this again");
        public bool DontShowAgain { get { return dontShow.Checked; } }
        public bool RunDemoRequested { get; private set; }
        public bool HooksAdded { get; private set; }   // asks for a restart when the window closes
        readonly List<Action> refreshers = new List<Action>();
        readonly Timer refresh = new Timer { Interval = 1500 };
        readonly Label error = new Label();

        sealed class Found
        {
            public string Agent, Title, AppId;   // AppId: a desktop app, watched automatically
            public Func<bool> Hooked;     // null: nothing to connect (watched automatically)
            public Action Connect;
            public string Consent;
        }

        public WelcomeForm(Settings settings, PetManager pets)
        {
            this.settings = settings;
            this.pets = pets;
            Text = "Welcome to OpaGachi";   // taskbar only: the glass window shows no title bar
            dontShow.Checked = !settings.ShowWelcome;

            // Layout (user, 2026-11-02: no icon, calmer spacing): one 24 px margin all round, the title and a line
            // of text, one glass card holding every found agent plus the demo, then the footer. Rows are 46 px.
            const int W = 520, M = 24, RowH = 46;
            var found = Detect();
            int cardTop = 108, rows = found.Count + 1;   // + the demo row
            cardBounds = new Rectangle(M, cardTop, W - 2 * M, rows * RowH);
            int footerTop = cardBounds.Bottom + 16;
            ClientSize = new Size(W, footerTop + 62);

            Controls.Add(new Label { Text = "Welcome to OpaGachi", Font = Theme.Heading(15F), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(M - 2, 28), BackColor = Color.Transparent });
            string pitch = found.Count == 0
                ? "Little desk pets above your taskbar that act out what your AI apps are doing. No AI apps found yet: open one and its pet appears."
                : "Little desk pets above your taskbar that act out what your AI apps are doing. Found on this PC:";
            Controls.Add(new Label { Text = pitch, ForeColor = Theme.TextSecondary, Font = Theme.Ui(9.5F), AutoSize = false, Size = new Size(W - 2 * M - 30, 40), Location = new Point(M, 62), BackColor = Color.Transparent });

            for (int i = 0; i < found.Count; i++) Controls.Add(BuildRow(found[i], new Rectangle(cardBounds.X, cardTop + i * RowH, cardBounds.Width, RowH), i == 0));

            // The demo, as the card's last row.
            var demoRow = new Panel { Bounds = new Rectangle(cardBounds.X, cardTop + found.Count * RowH, cardBounds.Width, RowH), BackColor = Color.Transparent };
            if (found.Count > 0) demoRow.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 16, 0, demoRow.Width - 16, 0); };
            demoRow.Controls.Add(new Label { Text = "See every pose first", Font = Theme.Ui(9.5F), ForeColor = Theme.TextSecondary, AutoSize = true, Location = new Point(16, 14), BackColor = Color.Transparent });
            var demo = new FlatButton("Watch the demo", 128) { Location = new Point(demoRow.Width - 16 - 128, 9) };
            demo.Click += (s, e) => { RunDemoRequested = true; Close(); };
            demoRow.Controls.Add(demo);
            Controls.Add(demoRow);

            // Footer: the checkbox and Get started on the same margins as the card.
            var footer = new Panel { Location = new Point(0, footerTop), Size = new Size(W, 62), BackColor = Color.Transparent };
            error.Location = new Point(M, 0);
            error.Size = new Size(W - 2 * M, 16);
            error.ForeColor = Theme.Danger;
            error.AutoEllipsis = true;
            error.BackColor = Color.Transparent;
            footer.Controls.Add(error);
            dontShow.Location = new Point(M - 3, 22);
            footer.Controls.Add(dontShow);
            var start = new FlatButton("Get started", 120) { Primary = true, Size = new Size(120, 32), Location = new Point(W - M - 120, 18) };
            start.Click += (s, e) => Close();
            footer.Controls.Add(start);
            Controls.Add(footer);
            AcceptButton = start;
            CancelButton = start;

            refresh.Tick += (s, e) => { foreach (var r in refreshers) r(); };
            refresh.Start();
            AddCaptionButtons(this, false);
            Drag(this);
            Drag(footer);
        }

        Rectangle cardBounds;

        // The found agents sit on one glass card (the rows are see-through).
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.Glass(e.Graphics, cardBounds, Theme.RadiusCard);
        }

        List<Found> Detect()
        {
            var list = new List<Found>();
            string claudePath = ClaudeCode.DefaultSettingsPath();
            if (Integrations.ClaudeCode)
                list.Add(new Found
                {
                    Agent = "claude", Title = "Claude Code",
                    Hooked = () => Integrations.ClaudeHooks,
                    Connect = () => ClaudeCode.InstallHooks(claudePath, settings.Port, Application.ExecutablePath),
                    Consent = "Add OpaGachi hooks to " + claudePath + "? They add approval prompts; sessions already show up without them. A backup is saved first.",
                });
            if (Integrations.ClaudeApp) list.Add(new Found { Agent = "claude", Title = "Claude app",AppId = "claude" });
            if (Integrations.ChatGptApp || Integrations.ChatGptClassic) list.Add(new Found { Agent = "chatgpt", Title = "ChatGPT app",AppId = "chatgpt" });
            if (Integrations.GeminiApp) list.Add(new Found { Agent = "gemini", Title = "Gemini app",AppId = "gemini" });
            if (Integrations.GrokApp) list.Add(new Found { Agent = "grok", Title = "Grok app", AppId = "grok" });
            if (GeminiHooks.CliInstalled)
                list.Add(new Found
                {
                    Agent = "gemini", Title = "Gemini CLI",
                    Hooked = () => Integrations.GeminiHook,
                    Connect = () => GeminiHooks.Install(GeminiHooks.DefaultPath(), Application.ExecutablePath),
                    Consent = "Add OpaGachi hooks to " + GeminiHooks.DefaultPath() + "? A backup is saved first.",
                });
            if (GrokHooks.CliInstalled)
                list.Add(new Found
                {
                    Agent = "grok", Title = "Grok Build",
                    Hooked = () => GrokHooks.Installed || Integrations.ClaudeHooks,
                    Connect = () => GrokHooks.Install(settings.Port),
                    Consent = "Add " + GrokHooks.FilePath + "? It's a new file of its own; nothing else changes.",
                });
            return list;
        }

        Control BuildRow(Found f, Rectangle bounds, bool first)
        {
            var row = new Panel { Bounds = bounds, BackColor = Color.Transparent };
            if (!first) row.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 16, 0, row.Width - 16, 0); };
            // Just the name and its state as a check-mark pill (user, 2026-10-31: no ties, dots or subtext).
            row.Controls.Add(new Label { Text = f.Title, Font = Theme.Semibold(9.5F), ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(16, 14), BackColor = Color.Transparent });
            var action = new FlatButton("", 128) { Location = new Point(bounds.Width - 16 - 128, 9) };
            row.Controls.Add(action);
            Action update = () =>
            {
                if (f.Hooked != null && !f.Hooked())
                {
                    action.Text = "Add hook";
                    action.Icon = null;
                    action.Primary = true;
                    action.Passive = false;
                }
                else Confirmed(action, f.Hooked != null ? "Connected" : "Watching");
                action.Invalidate();
            };
            action.Click += (s, e) =>
            {
                if (f.Connect == null) return;
                if (MessageBox.Show(this, f.Consent, "OpaGachi", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                try { f.Connect(); error.Text = ""; HooksAdded = true; }
                catch (Exception ex) { error.Text = "Couldn't connect: " + ex.Message; }
                update();
            };
            refreshers.Add(update);
            update();
            return row;
        }

        // Nothing to do on this row: a calm pill with a green check (Gemini Response 3), not a disabled button.
        static void Confirmed(FlatButton b, string text)
        {
            b.Text = text;
            b.Icon = "state-done";
            b.IconColor = Theme.Success;
            b.Primary = false;
            b.Passive = true;
        }

        // Entrance (Gemini Responses 3 and 5): the hero first, then each found agent rising 10 px into place,
        // 60 ms apart from +70 ms (the stagger stops growing after 240 ms); the demo row and footer follow.
        Entrance entrance;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (Theme.ReduceMotion) return;
            var bands = new List<Rectangle>();
            foreach (Control c in Controls)
                if (c is Panel && c.Top >= cardBounds.Top && c.Bottom <= cardBounds.Bottom) bands.Add(c.Bounds);   // the card's rows
            bands.Sort((a, b) => a.Top.CompareTo(b.Top));
            entrance = new Entrance(Entrance.Snapshot(this));
            entrance.Add(new Rectangle(0, 0, ClientSize.Width, cardBounds.Top), 0, 6);
            int delay = 70;
            foreach (var r in bands) { entrance.Add(r, delay, 10); delay = Math.Min(70 + 240, delay + 60); }
            int last = bands.Count > 0 ? bands[bands.Count - 1].Bottom : 88;
            entrance.Add(new Rectangle(0, last, ClientSize.Width, ClientSize.Height - last), delay, 10);
            entrance.Cover(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (entrance != null && !entrance.IsDisposed) entrance.Play();
            entrance = null;
        }

        // Get started: the window fades out over 100 ms before it closes.
        bool closing;
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (closing || e.Cancel || Theme.ReduceMotion || e.CloseReason != CloseReason.UserClosing && e.CloseReason != CloseReason.None) return;
            e.Cancel = true;
            closing = true;
            var fade = new Timer { Interval = 15 };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            fade.Tick += (s, a) =>
            {
                double t = clock.Elapsed.TotalMilliseconds / Theme.FastMs;
                if (t >= 1 || IsDisposed) { fade.Stop(); fade.Dispose(); if (!IsDisposed) Close(); return; }
                Opacity = 1 - t;
            };
            fade.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            refresh.Stop();
            refresh.Dispose();
            base.OnFormClosed(e);
        }
    }

    // Owner-drawn 16×16 checkbox + label (no light Win32 box in the dark window).
    sealed class CheckRow : FlatControl
    {
        bool isChecked;
        readonly Fade tick = new Fade(Theme.StandardMs);

        public CheckRow(string text)
        {
            Text = text;
            Size = new Size(220, 24);
            ForeColor = Theme.TextSecondary;
            TabStop = true;
        }

        public bool Checked
        {
            get { return isChecked; }
            set { isChecked = value; tick.On = value; if (!IsHandleCreated) tick.T = value ? 1 : 0; Animate(); }
        }

        protected override bool StepMore(double dt) { return tick.Step(dt); }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) Checked = !Checked;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(2.5f, Height / 2f - 8, 16, 16);
            double k = tick.Eased;
            using (var b = new SolidBrush(Theme.Mix(Theme.Mix(Theme.RaisedBottom, Theme.RaisedTop, Hover.Eased), Theme.Accent, k))) using (var p = Theme.Round(box, 5)) g.FillPath(b, p);
            using (var pen = new Pen(Theme.Mix(Theme.BorderStrong, Theme.AccentPressed, k))) using (var p = Theme.Round(box, 5)) g.DrawPath(pen, p);
            // The tick draws itself left to right as the box fills (and undraws when cleared).
            if (tick.T > 0.02)
            {
                var a = new PointF(box.X + 4, box.Y + 8.5f);
                var b = new PointF(box.X + 7, box.Y + 11.5f);
                var c = new PointF(box.X + 12.5f, box.Y + 5);
                double first = 4.24, second = 9.92, drawn = (first + second) * Theme.EaseOut(tick.T);
                using (var pen = Icons.RoundPen(Theme.AccentText, 2f))
                {
                    float f1 = (float)Math.Min(1, drawn / first);
                    g.DrawLine(pen, a, new PointF(a.X + (b.X - a.X) * f1, a.Y + (b.Y - a.Y) * f1));
                    if (drawn > first)
                    {
                        float f2 = (float)Math.Min(1, (drawn - first) / second);
                        g.DrawLine(pen, b, new PointF(b.X + (c.X - b.X) * f2, b.Y + (c.Y - b.Y) * f2));
                    }
                }
            }
            if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.Accent)) g.DrawRectangle(pen, box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(26, 0, Width - 26, Height), Theme.Mix(Theme.TextSecondary, Theme.TextPrimary, Hover.Eased), TextFormatFlags.VerticalCenter);
        }
    }
}
