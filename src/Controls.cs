using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpaGachi
{
    // Owner-drawn controls following the UI Design Rules (scheme A from Gemini Response 2): recessed
    // tracks, raised thumbs and buttons with a 1 px top highlight, one accent, hairlines, 4 px radii.
    // Every state change is a short colour/size transition driven by Animator.

    interface IAnimated { bool Step(double dt); }   // returns true while still moving

    // One shared ~60 fps timer that runs only while something is animating (zero cost at rest).
    static class Animator
    {
        static readonly Timer timer = new Timer { Interval = 15 };
        static readonly List<IAnimated> active = new List<IAnimated>();
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static double last;

        static Animator() { timer.Tick += (s, e) => Tick(); }

        public static void Start(IAnimated a)
        {
            if (!active.Contains(a)) active.Add(a);
            if (!timer.Enabled) { last = clock.Elapsed.TotalSeconds; timer.Start(); }
        }

        static void Tick()
        {
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.05, now - last);
            last = now;
            foreach (var a in active.ToArray())
            {
                var c = a as Control;
                bool keep;
                try { keep = (c == null || !c.IsDisposed) && a.Step(dt); }
                catch (Exception) { keep = false; }
                if (!keep) active.Remove(a);
            }
            if (active.Count == 0) timer.Stop();
        }
    }

    // 0..1 progress toward on/off over a fixed duration; read it eased. Movement fades (slides, growth)
    // jump straight to the end under reduced motion; colour fades shrink to 120 ms.
    sealed class Fade
    {
        public double T;
        public bool On, Movement;
        public int OffMs;   // optional duration back to off (press 50 ms, release 150 ms)
        readonly int ms;

        public Fade(int ms) { this.ms = Math.Max(1, ms); }
        public Fade(int ms, bool on) : this(ms) { On = on; T = on ? 1 : 0; }

        public double Eased { get { return Theme.EaseOut(T); } }
        public double Decel { get { return Theme.Decel(T); } }

        public bool Step(double dt)
        {
            double target = On ? 1 : 0;
            if (T == target) return false;
            int duration = Theme.Motion(On || OffMs <= 0 ? ms : OffMs, Movement);
            if (duration <= 0) { T = target; return false; }
            double d = dt * 1000 / duration;
            T = On ? Math.Min(1, T + d) : Math.Max(0, T - d);
            return T != target;
        }
    }

    abstract class FlatControl : Control, IAnimated
    {
        protected readonly Fade Hover = new Fade(Theme.StandardMs), Press = new Fade(Theme.PressMs) { OffMs = Theme.StandardMs };
        protected bool Hovered { get { return Hover.On; } }

        protected FlatControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;   // see-through: the glass card behind shows (liquid glass, 2026-11-01)
            ForeColor = Theme.TextPrimary;
            Font = Theme.Ui(9F);
            Cursor = Cursors.Hand;
        }

        protected void Animate() { Animator.Start(this); }

        public bool Step(double dt)
        {
            bool moving = Hover.Step(dt);
            moving |= Press.Step(dt);
            moving |= StepMore(dt);
            Invalidate();
            return moving;
        }

        protected virtual bool StepMore(double dt) { return false; }

        protected override void OnMouseEnter(EventArgs e) { Hover.On = true; Animate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover.On = false; Press.On = false; Animate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Press.On = true; Animate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Press.On = false; Animate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected static GraphicsPath Pill(RectangleF r)
        {
            var p = new GraphicsPath();
            float d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }

        protected static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var b = new SolidBrush(c)) using (var p = Theme.Round(r, radius)) g.FillPath(b, p);
        }

        protected static void DrawRound(Graphics g, Color c, RectangleF r, float radius, float width)
        {
            using (var pen = new Pen(c, width)) using (var p = Theme.Round(r, radius)) g.DrawPath(pen, p);
        }
    }

    // Aqua gel pill (Gemini Response 4, 8E/8F): a vertical gradient body, a 1 px offset drop shadow, a white
    // top-half gloss and a white inner rim; pressing flips into the shadow. Primary = gel blue (one-click
    // upgrades: Add hook, Connect), secondary = white-to-silver (graphite on Dark), disabled = flat and muted, Passive = a
    // status shown in button form ("✓ Connected"), never clickable. Labels morph when they change (fixed
    // width, so nothing jitters). Also used for dropdowns (Chevron) and the tie colour swatch (Swatch).
    sealed class FlatButton : FlatControl, IButtonControl
    {
        public bool Chevron;
        public string Icon;        // optional line icon (Icons.cs) before the label
        public Color? IconColor;   // defaults to the label colour
        bool primary, passive;
        Color? swatch;
        string shownText = "", oldText = "";
        readonly Fade prim = new Fade(Theme.StandardMs), morph = new Fade(180) { Movement = true },
            pop = new Fade(160) { Movement = true };

        public FlatButton(string text, int width)
        {
            Text = text;
            shownText = text;
            Size = new Size(width, 28);
            TabStop = true;
            morph.On = true; morph.T = 1;
            pop.On = true; pop.T = 1;
        }

        public bool Primary
        {
            get { return primary; }
            set { if (primary == value) return; primary = value; prim.On = value; if (!IsHandleCreated) prim.T = value ? 1 : 0; Animate(); }
        }

        public bool Passive
        {
            get { return passive; }
            set { passive = value; Cursor = value ? Cursors.Default : Cursors.Hand; TabStop = !value; Invalidate(); }
        }

        // Colour swatch; pops 1 → 1.12 → 1 when the colour changes.
        public Color? Swatch
        {
            get { return swatch; }
            set
            {
                bool changed = swatch.HasValue && value.HasValue && swatch.Value.ToArgb() != value.Value.ToArgb();
                swatch = value;
                if (changed && IsHandleCreated) { pop.T = 0; pop.On = true; Animate(); }
                Invalidate();
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            if (IsHandleCreated && Visible && shownText.Length > 0 && shownText != Text)
            {
                oldText = shownText;
                morph.T = 0;
                morph.On = true;
                Animate();
            }
            shownText = Text;
            base.OnTextChanged(e);
        }

        protected override bool StepMore(double dt)
        {
            bool moving = prim.Step(dt);
            moving |= morph.Step(dt);
            moving |= pop.Step(dt);
            return moving;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            double h = passive ? 0 : Hover.Eased, p = passive ? 0 : Press.Eased, pr = prim.Eased;

            if (swatch.HasValue)
            {
                float grow = pop.T < 1 ? (float)(0.12 * Math.Sin(Math.PI * pop.T)) : 0;
                var sr = RectangleF.Inflate(r, -2 + grow * Width / 2, -2 + grow * Height / 2);
                FillRound(g, Enabled ? swatch.Value : Theme.Mix(swatch.Value, Theme.Surface, 0.6), sr, 3);
                Theme.Gloss(g, sr, 3, Enabled ? Theme.GlossTopRest : 0);
                DrawRound(g, Theme.Mix(Theme.BorderStrong, Theme.TextSecondary, h), sr, 3, 1);
                if (Focused && ShowFocusCues) DrawRound(g, Theme.Accent, RectangleF.Inflate(r, -3, -3), 2, 1);
                return;
            }

            Color top, bottom, border, text;
            int gloss, rim;
            if (!Enabled)
            {
                top = bottom = Theme.Well; border = Theme.Border; text = Theme.TextMuted; gloss = rim = 0;
            }
            else if (passive)
            {
                top = Theme.RaisedTop; bottom = Theme.Mix(Theme.RaisedTop, Theme.RaisedBottom, 0.5); border = Theme.Border; text = Theme.TextSecondary;
                gloss = Theme.Dark ? Theme.GlossTopRest / 2 : 0; rim = 0;
            }
            else
            {
                // Hover brightens toward the hover tint (secondary) or a lighter gel (primary); press flips the gradient.
                Color secTop = Theme.Mix(Theme.RaisedTop, Theme.Hover, 0.5 * h), secBottom = Theme.Mix(Theme.RaisedBottom, Theme.Hover, 0.6 * h);
                Color priTop = Theme.Mix(Theme.GelTop, Color.White, 0.15 * h), priBottom = Theme.Mix(Theme.GelBottom, Theme.GelTop, 0.12 * h);
                top = Theme.Mix(Theme.Mix(secTop, priTop, pr), Theme.Mix(secBottom, Theme.GelPressed, pr), p * 0.8);
                bottom = Theme.Mix(Theme.Mix(secBottom, priBottom, pr), Theme.Mix(secTop, priBottom, pr), p * 0.5);
                Color secBorder = Theme.Mix(Theme.BorderStrong, Theme.BorderHover, h);
                border = Theme.Mix(secBorder, Theme.GelPressed, pr);
                Color secText = Chevron ? Theme.TextPrimary : Theme.Mix(Theme.TextPrimary, Theme.TextSecondary, p * 0.5);
                text = Theme.Mix(secText, Theme.AccentText, pr);
                int secGloss = Theme.Dark ? Theme.GlossTopRest + (int)((Theme.GlossTopHover - Theme.GlossTopRest) * h) : 230;
                gloss = (int)Math.Round((secGloss + (150 - secGloss) * pr) * (1 - p));
                rim = (int)Math.Round((Theme.Rim + (90 - Theme.Rim) * pr) * (1 - p));
            }
            Color fill = Theme.Mix(top, bottom, 0.5);   // labels cross-fade from this
            var body = new RectangleF(0.5f, 0.5f, Width - 1, Height - 2);
            // Gel press (Gemini Response 5): the body shrinks to 0.96 and the inner rim drops 1 px.
            body = RectangleF.Inflate(body, -0.02f * body.Width * (float)p, -0.02f * body.Height * (float)p);
            float radius = body.Height / 2;   // capsules everywhere, dropdowns included
            if (Enabled && !passive && p < 0.5)
                FillRound(g, Theme.DropShadow, new RectangleF(body.X, body.Y + 1, body.Width, body.Height), radius);
            using (var shape = Theme.Round(body, radius)) Theme.Gradient(g, shape, body, top, bottom);
            Theme.Gloss(g, body, radius, gloss);
            var rimRect = RectangleF.Inflate(body, -1, -1);
            rimRect.Offset(0, (float)p);
            if (rim > 0) DrawRound(g, Color.FromArgb(Math.Min(255, rim), 255, 255, 255), rimRect, radius - 1, 1);
            if (p > 0.3) Theme.InnerShadow(g, body, radius);
            DrawRound(g, border, body, radius, 1);
            if (Focused && ShowFocusCues) DrawRound(g, pr > 0.5 ? Theme.AccentText : Theme.Accent, RectangleF.Inflate(body, -2, -2), radius - 2, 1);

            var font = pr > 0.5 ? Theme.Semibold(9F) : Font;
            try
            {
                if (Chevron)
                {
                    TextRenderer.DrawText(g, Text, font, new Rectangle(10, 0, Width - 30, Height), text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    float cx = Width - 15, cy = Height / 2f - 0.5f;
                    if (!Theme.Dark) using (var emboss = Icons.RoundPen(Color.White, 2f))
                        g.DrawLines(emboss, new[] { new PointF(cx - 5, cy - 1.5f), new PointF(cx, cy + 3.5f), new PointF(cx + 5, cy - 1.5f) });
                    using (var pen = Icons.RoundPen(Theme.TextSecondary, 1.5f))
                        g.DrawLines(pen, new[] { new PointF(cx - 5, cy - 2.5f), new PointF(cx, cy + 2.5f), new PointF(cx + 5, cy - 2.5f) });
                    return;
                }
                // Label morph: the old label fades out in 80 ms, the new one rises 5 px into place over 120 ms.
                double ms = morph.T * 180;
                if (morph.T < 1 && oldText.Length > 0)
                    DrawLabel(g, oldText, font, Theme.Mix(fill, text, 1 - Theme.Clamp01(ms / 80)), 0);
                double kIn = Theme.Decel((ms - 60) / 120);
                DrawLabel(g, Text, font, morph.T < 1 ? Theme.Mix(fill, text, kIn) : text, morph.T < 1 ? (float)(5 * (1 - kIn)) : 0);
            }
            finally { if (font != Font) font.Dispose(); }
        }

        void DrawLabel(Graphics g, string label, Font font, Color color, float dy)
        {
            Color iconColor = IconColor.HasValue && Enabled ? IconColor.Value : color;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            int textW = TextRenderer.MeasureText(g, label, font, new Size(Width, Height), flags).Width;
            int iconW = Icon != null ? 14 + 6 : 0;
            int x = Math.Max(6, (Width - textW - iconW) / 2);
            if (Icon != null) Icons.Draw(g, Icon, x, Height / 2f - 7 + dy, 14, iconColor, 1.4f);
            TextRenderer.DrawText(g, label, font, new Rectangle(x + iconW, (int)Math.Round(dy), Width - x - iconW - 4, Height), color, flags);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { PerformClick(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled && !passive) { Focus(); base.OnClick(e); }
        }

        // IButtonControl, so the form's Enter/Escape keys work with it.
        public DialogResult DialogResult { get; set; }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled && !passive) OnClick(EventArgs.Empty); }
    }

    // Horizontal slider. With TickLabels it snaps to one step per label and prints them underneath.
    sealed class FlatSlider : FlatControl
    {
        int minimum, maximum = 100, value;
        bool dragging;
        public string[] TickLabels;
        public string Suffix = "%";
        public bool ShowValue = true;
        public event EventHandler ValueChanged, Committed;   // Committed: drag released or key pressed
        readonly Spring thumb = new Spring(0, Feel.Snappy);   // the thumb glides to clicked positions
        readonly Fade flash = new Fade(Theme.StandardMs);   // stepped slider: the fill flashes lighter as it snaps to a tick

        public FlatSlider()
        {
            TabStop = true;
            Size = new Size(280, 26);
        }

        public int Minimum { get { return minimum; } set { minimum = value; Invalidate(); } }
        public int Maximum { get { return maximum; } set { maximum = value; Invalidate(); } }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(minimum, Math.Min(maximum, value));
                if (v == this.value) return;
                this.value = v;
                if (dragging || !IsHandleCreated) thumb.Snap(v); else thumb.Target = v;
                if (TickLabels != null && IsHandleCreated) { flash.T = 1; flash.On = false; }
                Animate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override bool StepMore(double dt) { return flash.Step(dt) | thumb.Step(dt); }

        int ValueWidth { get { return ShowValue && TickLabels == null ? 46 : 0; } }
        RectangleF Track { get { return new RectangleF(9, 11, Width - 18 - ValueWidth, 4); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var t = Track;
            float ratio = maximum == minimum ? 0 : (float)Theme.Clamp01((thumb.Value - minimum) / (maximum - minimum));
            float tx = t.X + ratio * t.Width;
            double h = Math.Max(Hover.Eased, dragging ? 1 : 0);
            using (var path = Pill(t))
            {
                if (Enabled) Theme.Gradient(g, path, t, Theme.TrackTop, Theme.TrackBottom);
                else using (var groove = new SolidBrush(Theme.Canvas)) g.FillPath(groove, path);
                using (var edge = new Pen(Theme.BorderStrong, 0.8f)) g.DrawPath(edge, path);
            }
            if (tx > t.X + 1)
            {
                var filled = new RectangleF(t.X, t.Y, tx - t.X, t.Height);
                Color top = Theme.Mix(Theme.Mix(Theme.GelTop, Color.White, 0.15 * h + 0.3 * flash.T), Theme.GelBottom, 0.3 * Press.Eased);
                using (var path = Pill(filled))
                {
                    if (Enabled) Theme.Gradient(g, path, filled, top, Theme.Mix(Theme.GelBottom, Theme.GelPressed, Press.Eased));
                    else using (var fill = new SolidBrush(Theme.BorderStrong)) g.FillPath(fill, path);
                    if (Enabled) using (var edge = new Pen(Theme.GelPressed, 0.8f)) g.DrawPath(edge, path);
                }
            }

            if (TickLabels != null)
            {
                using (var semibold = Theme.Semibold(8.5F))
                using (var regular = Theme.Ui(8.5F))
                using (var tick = new Pen(Theme.BorderStrong, 2))
                {
                    for (int i = 0; i < TickLabels.Length; i++)
                    {
                        float x = t.X + (TickLabels.Length == 1 ? 0 : i * t.Width / (TickLabels.Length - 1));
                        bool active = i == value;
                        if (Math.Abs(x - tx) > 8) g.DrawLine(tick, x, t.Y - 1, x, t.Y + t.Height + 1);
                        var font = active ? semibold : regular;
                        var size = TextRenderer.MeasureText(TickLabels[i], font);
                        var rect = new Rectangle((int)(x - size.Width / 2f), 23, size.Width, size.Height);
                        rect.X = Math.Max(0, Math.Min(Width - rect.Width, rect.X));
                        TextRenderer.DrawText(g, TickLabels[i], font, rect, active ? Theme.TextPrimary : Theme.TextSecondary,
                            TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter);
                    }
                }
            }
            else if (ShowValue)
            {
                var r = new Rectangle(Width - ValueWidth, 3, ValueWidth, 20);
                using (var mono = Theme.Ui(9F))
                    TextRenderer.DrawText(g, value + Suffix, mono, r, Theme.TextPrimary, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }

            // Glass orb thumb (Gemini Response 4, 8B): grows from 14 to 16 px, with a 0 → 4 px accent halo on hover or drag.
            float d = (float)(14 + 2 * h);
            var knob = new RectangleF(tx - d / 2, t.Y + t.Height / 2 - d / 2, d, d);
            if (Enabled && h > 0.02)
                using (var halo = new SolidBrush(Color.FromArgb((int)(60 * h), Theme.Accent))) g.FillEllipse(halo, RectangleF.Inflate(knob, (float)(4 * h), (float)(4 * h)));
            Theme.Orb(g, knob, Enabled, Press.On);
            if (Focused && ShowFocusCues && h < 0.05)
                using (var p = new Pen(Theme.Accent, 1.5f)) g.DrawEllipse(p, RectangleF.Inflate(knob, 2, 2));
        }

        void SetFromX(int x)
        {
            var t = Track;
            float ratio = Math.Max(0, Math.Min(1, (x - t.X) / t.Width));
            Value = minimum + (int)Math.Round(ratio * (maximum - minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Focus();
            SetFromX(e.X);   // glides there
            dragging = true;
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) SetFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging)
            {
                dragging = false;
                Animate();
                if (Committed != null) Committed(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int before = value;
            if (e.KeyCode == Keys.Left) Value = value - 1;
            if (e.KeyCode == Keys.Right) Value = value + 1;
            if (value != before && Committed != null) Committed(this, EventArgs.Empty);
            base.OnKeyDown(e);
        }
    }

    // On/off switch: 40×20 pill, recessed when off, accent when on; the knob slides between them on the
    // Snappy spring, so flicking it back mid-slide reverses with its momentum.
    sealed class ToggleSwitch : FlatControl
    {
        bool isOn;
        readonly Spring knob = new Spring(0, Feel.Snappy);
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            TabStop = true;
            Size = new Size(44, 24);
        }

        public bool Checked
        {
            get { return isOn; }
            set
            {
                if (value == isOn) return;
                isOn = value;
                if (IsHandleCreated) knob.Target = value ? 1 : 0; else knob.Snap(value ? 1 : 0);   // initial state: no slide
                Animate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override bool StepMore(double dt) { return knob.Step(dt); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double k = Theme.Clamp01(knob.Value), h = Hover.Eased;
            var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Pill(track))
            {
                // Off: recessed silver groove (Gemini Response 4, 8A). On: gel blue with a white gloss, faded in by k.
                if (Enabled) Theme.Gradient(g, path, track, Theme.TrackTop, Theme.TrackBottom);
                else using (var b = new SolidBrush(Theme.Well)) g.FillPath(b, path);
                if (Enabled && k > 0.01)
                {
                    Color top = Theme.Mix(Theme.GelTop, Color.White, 0.12 * h), bottom = Theme.Mix(Theme.GelBottom, Theme.GelPressed, Press.Eased);
                    using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(track.X, track.Y - 0.5f, track.Width, track.Height + 1),
                               Color.FromArgb((int)(255 * k), top), Color.FromArgb((int)(255 * k), bottom), 90f)) g.FillPath(b, path);
                    Theme.Gloss(g, track, track.Height / 2, (int)(150 * k));
                }
                if (Enabled && k < 0.5) Theme.InnerShadow(g, track, track.Height / 2);
                Color edge = !Enabled ? Theme.Border : Theme.Mix(Focused && ShowFocusCues ? Theme.Accent : Theme.BorderHover, Theme.GelPressed, k);
                using (var p = new Pen(edge)) g.DrawPath(p, path);
            }
            // Glass orb thumb slides from left to right.
            float d = Height - 4;
            float cx = (float)(2 + d / 2 + (Width - d - 4.5f) * k);
            Theme.Orb(g, new RectangleF(cx - d / 2, 1.5f, d, d), Enabled, Press.On);
        }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            Focus();
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && Enabled) Checked = !Checked;
            base.OnKeyDown(e);
        }
    }

    // [−] value [+]: raised side buttons around a recessed mono value (120×28).
    sealed class Stepper : FlatControl
    {
        int value = 1;
        public int Minimum = 1, Maximum = 120;
        public string Unit = "min";
        public event EventHandler ValueChanged;
        int hot = 0, down = 0;   // -1 minus, +1 plus, 0 none
        int oldValue, rollDir;   // odometer roll (Snappy): the old number leaves 12 px up (or down), the new one rolls in
        readonly Spring roll = new Spring(1, Feel.Snappy);   // 0 → 1 per change

        public Stepper()
        {
            Size = new Size(120, 28);
            TabStop = true;
        }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(Minimum, Math.Min(Maximum, value));
                if (v == this.value) { Invalidate(); return; }
                if (IsHandleCreated) { oldValue = this.value; rollDir = v > this.value ? 1 : -1; roll.Snap(0); roll.Target = 1; Animate(); }
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override bool StepMore(double dt) { return roll.Step(dt); }

        int Zone(int x) { return x < 28 ? -1 : x >= Width - 28 ? 1 : 0; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int z = Zone(e.X);
            if (z != hot) { hot = z; Invalidate(); }
            Cursor = z == 0 ? Cursors.Default : Cursors.Hand;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = down = 0; base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            down = Zone(e.X);
            Focus();
            if (down != 0) Value = value + down;
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { down = 0; base.OnMouseUp(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value = value - 1;
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value = value + 1;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float radius = r.Height / 2;
            using (var shape = Theme.Round(r, radius)) Theme.Gradient(g, shape, r, Theme.RaisedTop, Theme.RaisedBottom);
            Theme.Gloss(g, r, radius, Theme.Dark ? Theme.GlossTopRest : 120);
            DrawSide(g, new RectangleF(0.5f, 0.5f, 28, Height - 1), -1, "−", value <= Minimum);
            DrawSide(g, new RectangleF(Width - 28.5f, 0.5f, 28, Height - 1), 1, "+", value >= Maximum);
            DrawRound(g, Focused && ShowFocusCues ? Theme.Accent : Theme.BorderStrong, r, radius, 1);
            var well = new Rectangle(29, 1, Width - 58, Height - 2);
            var old = g.Clip;
            g.SetClip(well);
            using (var mono = Theme.Ui(9F))
            {
                double k = Theme.Clamp01(roll.Value);
                if (roll.Value < 1)
                {
                    DrawValue(g, mono, oldValue, Theme.Mix(Theme.TextPrimary, Theme.Well, Theme.Clamp01(k * 1.6)), (int)Math.Round(-12 * rollDir * k));
                    DrawValue(g, mono, value, Theme.Mix(Theme.Well, Theme.TextPrimary, k), (int)Math.Round(12 * rollDir * (1 - k)));
                }
                else DrawValue(g, mono, value, Theme.TextPrimary, 0);
            }
            g.Clip = old;
        }

        void DrawValue(Graphics g, Font mono, int v, Color color, int dy)
        {
            TextRenderer.DrawText(g, v + " " + Unit, mono, new Rectangle(28, dy, Width - 56, Height), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void DrawSide(Graphics g, RectangleF r, int zone, string glyph, bool atLimit)
        {
            Color fill = atLimit ? Color.Transparent : down == zone ? Theme.TrackTop : hot == zone ? Theme.RowHoverTint : Color.Transparent;
            using (var clip = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), (Height - 1) / 2f))
            {
                var old = g.Clip;
                g.SetClip(clip);
                using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
                g.Clip = old;
            }
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, zone < 0 ? r.Right : r.X, 1, zone < 0 ? r.Right : r.X, Height - 2);
            using (var font = Theme.Ui(10F))
                TextRenderer.DrawText(g, glyph, font, Rectangle.Round(r), atLimit ? Theme.TextMuted : down == zone ? Theme.TextSecondary : Theme.TextPrimary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Top tabs (user, 2026-11-01): a floating glass capsule; the selected tab is a gel lens that slides to the
    // picked tab on the Smooth spring (position and width both spring, so a long tab stretches it). Bare parts
    // of the bar drag the window. Left/Right step through the tabs.
    sealed class TabBar : Control, IAnimated
    {
        const int TabH = 30, Pad = 4;
        readonly string[] names, icons;
        readonly Spring lensX = new Spring(0, Feel.Smooth), lensW = new Spring(0, Feel.Smooth);
        readonly Fade[] hover;
        Rectangle[] tabs = new Rectangle[0];
        Rectangle capsule;
        int selected = -1;
        public event Action<int> Picked;
        public event MouseEventHandler BarePress;   // a press outside the tabs (the window drags from there)

        public TabBar(string[] names, string[] icons)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            this.names = names;
            this.icons = icons;
            hover = names.Select(n => new Fade(Theme.StandardMs)).ToArray();
            BackColor = Color.Transparent;
            Font = Theme.Semibold(9F);
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTabList;
        }

        public int Selected
        {
            get { return selected; }
            set
            {
                selected = value;
                Measure();
                if (selected < 0 || selected >= tabs.Length) return;
                AccessibleName = names[selected] + " tab";
                bool snap = !IsHandleCreated || !Visible || lensW.Value <= 0;
                if (snap) { lensX.Snap(tabs[selected].X); lensW.Snap(tabs[selected].Width); }
                else { lensX.Target = tabs[selected].X; lensW.Target = tabs[selected].Width; Animator.Start(this); }
                Invalidate();
            }
        }

        bool showIcons = true;

        // Centred, clear of the caption buttons on the right (kept symmetric): large text drops the icons first,
        // then tightens the padding.
        void Measure()
        {
            int room = Width - 2 * 78;
            int[] widths = null;
            foreach (var fit in new[] { new[] { 1, 22 }, new[] { 1, 14 }, new[] { 0, 20 }, new[] { 0, 10 } })
            {
                showIcons = icons != null && fit[0] == 1;
                widths = names.Select(n => TextRenderer.MeasureText(n, Font).Width + (showIcons ? 20 : 0) + fit[1]).ToArray();
                if (widths.Sum() + 2 * Pad <= room) break;
            }
            int total = widths.Sum() + 2 * Pad;
            capsule = new Rectangle((Width - total) / 2, (Height - TabH - 2 * Pad) / 2, total, TabH + 2 * Pad);
            tabs = new Rectangle[names.Length];
            int x = capsule.X + Pad;
            for (int i = 0; i < names.Length; i++) { tabs[i] = new Rectangle(x, capsule.Y + Pad, widths[i], TabH); x += widths[i]; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Measure();
            if (selected >= 0 && selected < tabs.Length) { lensX.Snap(tabs[selected].X); lensW.Snap(tabs[selected].Width); }
        }

        public bool Step(double dt)
        {
            bool moving = lensX.Step(dt) | lensW.Step(dt);
            foreach (var h in hover) moving |= h.Step(dt);
            Invalidate();
            return moving;
        }

        int Hit(Point p) { for (int i = 0; i < tabs.Length; i++) if (tabs[i].Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hot = Hit(e.Location);
            for (int i = 0; i < hover.Length; i++) hover[i].On = i == hot;
            Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
            Animator.Start(this);
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            foreach (var h in hover) h.On = false;
            Animator.Start(this);
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = Hit(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left) { if (Picked != null) Picked(i); }
            else if (BarePress != null) BarePress(this, e);
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0;
            if (step != 0 && Picked != null) Picked((selected + step + names.Length) % names.Length);
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Glass(g, capsule, capsule.Height / 2f);
            for (int i = 0; i < tabs.Length; i++)
                if (hover[i].T > 0.01 && i != selected)
                    using (var b = new SolidBrush(Theme.Mix(Color.FromArgb(0, Theme.RowHoverTint), Theme.RowHoverTint, hover[i].Eased))) using (var p = Theme.Round(tabs[i], TabH / 2f)) g.FillPath(b, p);

            // The lens: gel body, gloss, white rim.
            var lens = new RectangleF((float)lensX.Value, capsule.Y + Pad, (float)Math.Max(TabH, lensW.Value), TabH);
            if (selected >= 0)
            {
                using (var shape = Theme.Round(lens, TabH / 2f)) Theme.Gradient(g, shape, lens, Theme.GelTop, Theme.GelBottom);
                Theme.Gloss(g, lens, TabH / 2f, 120);
                using (var rim = new Pen(Color.FromArgb(Theme.HighContrast ? 0 : 110, 255, 255, 255))) using (var p = Theme.Round(RectangleF.Inflate(lens, -1, -1), TabH / 2f - 1)) g.DrawPath(rim, p);
                if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.AccentText)) using (var p = Theme.Round(RectangleF.Inflate(lens, -3, -3), TabH / 2f - 3)) g.DrawPath(pen, p);
            }

            // Labels go white where the lens covers them.
            for (int i = 0; i < tabs.Length; i++)
            {
                var t = tabs[i];
                double cover = Theme.Clamp01((Math.Min(t.Right, lens.Right) - Math.Max(t.X, lens.X)) / (double)t.Width);
                Color c = Theme.Mix(Theme.Mix(Theme.TextSecondary, Theme.TextPrimary, hover[i].Eased), Theme.AccentText, cover);
                int textW = TextRenderer.MeasureText(names[i], Font).Width, x = t.X + (t.Width - textW - (showIcons ? 20 : 0)) / 2;
                if (showIcons) { Icons.Draw(g, icons[i], x, t.Y + TabH / 2f - 7, 14, c, 1.4f); x += 20; }
                TextRenderer.DrawText(g, names[i], Font, new Rectangle(x, t.Y, textW + 2, TabH), c, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // A round glass caption button (close, minimise) for the borderless windows; close glows red on hover.
    sealed class CaptionButton : FlatControl
    {
        readonly bool close;

        public CaptionButton(bool close)
        {
            this.close = close;
            Size = new Size(26, 26);
            AccessibleName = close ? "Close" : "Minimise";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 2, Height - 2);
            double h = Hover.Eased;
            Theme.Glass(g, r, r.Height / 2);
            if (h > 0.01)
                using (var b = new SolidBrush(Theme.Mix(Color.FromArgb(0, close ? Theme.Danger : Theme.RowHoverTint), close ? Theme.Danger : Theme.RowHoverTint, h))) g.FillEllipse(b, r);
            Color c = close ? Theme.Mix(Theme.TextSecondary, Color.White, h) : Theme.Mix(Theme.TextSecondary, Theme.TextPrimary, h);
            float cx = Width / 2f, cy = Height / 2f, a = 4f;
            using (var pen = Icons.RoundPen(c, 1.6f))
            {
                if (close) { g.DrawLine(pen, cx - a, cy - a, cx + a, cy + a); g.DrawLine(pen, cx + a, cy - a, cx - a, cy + a); }
                else g.DrawLine(pen, cx - a, cy, cx + a, cy);
            }
        }
    }

    // Borderless glass window (user, 2026-11-01: liquid glass, no title, can't be maximised or resized): paints
    // the backdrop, rounds its corners on Windows 11, casts a shadow, and drags from any bare spot passed to Drag.
    class GlassForm : Form
    {
        protected GlassForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;   // no maximising, not even by double-clicking the drag area or Win+Up
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Canvas;
            ForeColor = Theme.TextPrimary;
            Font = Theme.Ui(9F);
            string icon = System.IO.Path.Combine(System.IO.Path.Combine(Program.Root, "assets"), "icon.ico");
            if (System.IO.File.Exists(icon)) Icon = new Icon(icon);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;            // CS_DROPSHADOW
                cp.Style |= 0x20000 | 0x80000;       // WS_MINIMIZEBOX | WS_SYSMENU: the taskbar button can minimise it
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.RoundCorners(Handle);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Theme.Backdrop(e.Graphics, ClientSize, e.ClipRectangle); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.BorderStrong)) e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);   // an edge on any wallpaper
        }

        // Pressing `c` (where nothing else catches the click) moves the window.
        public void Drag(Control c)
        {
            c.MouseDown += (s, e) => StartDrag(e);
        }

        public void StartDrag(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
        }

        // Close and minimise, top right.
        protected void AddCaptionButtons(Control host, bool minimise)
        {
            var close = new CaptionButton(true) { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            close.Location = new Point(host.Width - close.Width - 14, 14);
            close.Click += (s, e) => Close();
            host.Controls.Add(close);
            if (!minimise) return;
            var min = new CaptionButton(false) { Anchor = AnchorStyles.Top | AnchorStyles.Right };
            min.Location = new Point(close.Left - min.Width - 6, 14);
            min.Click += (s, e) => WindowState = FormWindowState.Minimized;
            host.Controls.Add(min);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    // A scrolling page. It paints its own share of the backdrop in page coordinates, so the backdrop scrolls with
    // the content and Windows can simply move the pixels on a scroll step; only the strip that comes into view is
    // drawn (user, 2026-11-02: repainting everything per step lagged and tore).
    sealed class GlassScroller : Panel
    {
        public GlassScroller()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Theme.Canvas;
            AutoScroll = true;
            Dock = DockStyle.Fill;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var form = FindForm();
            if (form == null) { base.OnPaintBackground(e); return; }
            var at = form.PointToClient(PointToScreen(Point.Empty));
            var scroll = AutoScrollPosition;   // negative while scrolled
            Theme.Backdrop(e.Graphics, form.ClientSize, e.ClipRectangle, new Point(at.X - scroll.X, at.Y - scroll.Y), at.Y + Math.Max(ClientSize.Height, DisplayRectangle.Height));
        }
    }

    // One tab in iOS Settings style (user, 2026-11-02): a root page listing categories; opening one slides its
    // page in from the right on the Smooth spring, Back (or clicking the tab again) slides it out. Category
    // pages are built the first time they're opened.
    sealed class DrillPage : Panel
    {
        Control root, shown;
        Func<Control>[] builders = new Func<Control>[0];
        Control[] built = new Control[0];
        public int OpenIndex { get; private set; }

        public DrillPage()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Dock = DockStyle.Fill;
            OpenIndex = -1;
        }

        public void SetRoot(Control page, Func<Control>[] categories)
        {
            root = shown = page;
            builders = categories;
            built = new Control[categories.Length];
            Controls.Add(page);
        }

        // -1 is the root list.
        public void Open(int index, bool animate)
        {
            if (index == OpenIndex || index >= builders.Length) return;
            Control next = index < 0 ? root : built[index] ?? (built[index] = builders[index]());
            PageTransition overlay = null;
            if (animate && Visible && !Theme.ReduceMotion && Width > 0 && Height > 0)
            {
                var before = new Bitmap(Width, Height);
                DrawToBitmap(before, ClientRectangle);
                overlay = new PageTransition(before, index > OpenIndex ? 1 : -1, true);
                Controls.Add(overlay);
                overlay.BringToFront();
                overlay.Refresh();
            }
            SuspendLayout();
            Controls.Remove(shown);
            Controls.Add(next);
            if (overlay != null) overlay.BringToFront();
            ResumeLayout(true);
            shown = next;
            OpenIndex = index;
            if (index < 0) root.Invalidate(true);   // values shown on the list may have changed
            if (overlay != null)
            {
                var after = new Bitmap(Width, Height);
                next.DrawToBitmap(after, ClientRectangle);
                overlay.Start(after);
            }
        }
    }

    // A category on a tab's root list: a tinted icon tile, the title, its current value in grey and a chevron.
    // Hover sheen; Enter or Space opens it.
    sealed class CategoryRow : Control, IAnimated
    {
        readonly string title, icon;
        readonly Color tint;
        readonly Func<string> value;
        readonly Fade hover = new Fade(Theme.StandardMs);

        public CategoryRow(string title, string icon, Color tint, Func<string> value)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                     | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            this.title = title;
            this.icon = icon;
            this.tint = tint;
            this.value = value;
            BackColor = Color.Transparent;
            Dock = DockStyle.Fill;
            Height = 50;
            Margin = new Padding(0);
            Font = Theme.Ui(10F);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = title;
        }

        public bool Step(double dt) { bool moving = hover.Step(dt); Invalidate(); return moving; }

        protected override void OnMouseEnter(EventArgs e) { hover.On = true; Animator.Start(this); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.On = false; Animator.Start(this); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Focus(); base.OnClick(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (hover.T > 0.01)
                using (var b = new SolidBrush(Theme.Mix(Color.FromArgb(0, Theme.RowHoverTint), Theme.RowHoverTint, hover.Eased))) using (var p = Theme.Round(new RectangleF(6, 3, Width - 12, Height - 6), 10)) g.FillPath(b, p);
            var tile = new RectangleF(16, (Height - 28) / 2f, 28, 28);
            using (var shape = Theme.Round(tile, 7)) Theme.Gradient(g, shape, tile, Theme.Mix(tint, Color.White, 0.18), tint);
            Theme.Gloss(g, tile, 7, 70);
            Icons.Draw(g, icon, tile.X + 6, tile.Y + 6, 16, Color.White, 1.6f);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            string v = value != null ? value() : "";
            int vw = string.IsNullOrEmpty(v) ? 0 : TextRenderer.MeasureText(g, v, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            TextRenderer.DrawText(g, title, Font, new Rectangle(56, 0, Width - 56 - vw - 48, Height), Theme.TextPrimary, flags);
            if (vw > 0) TextRenderer.DrawText(g, v, Font, new Rectangle(Width - 36 - vw, 0, vw + 2, Height), Theme.TextSecondary, flags);
            float cx = Width - 22, cy = Height / 2f;
            using (var pen = Icons.RoundPen(Theme.Mix(Theme.TextMuted, Theme.TextSecondary, hover.Eased), 1.6f))
                g.DrawLines(pen, new[] { new PointF(cx - 2.5f, cy - 5), new PointF(cx + 2.5f, cy), new PointF(cx - 2.5f, cy + 5) });
            if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.Accent)) using (var p = Theme.Round(new RectangleF(6.5f, 3.5f, Width - 13, Height - 7), 10)) g.DrawPath(pen, p);
        }
    }

    // The top of a category page: a "‹ Pets" back capsule and the category's title.
    sealed class BackHeader : Panel
    {
        public BackHeader(string tab, string title, Action back)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Dock = DockStyle.Fill;
            Height = 50;
            Margin = new Padding(0);
            string label = "‹  " + tab;
            int width;
            using (var f = Theme.Ui(9F)) width = TextRenderer.MeasureText(label, f).Width + 30;
            var button = new FlatButton(label, width) { Location = new Point(GlassCard.Side, 14) };
            button.Click += (s, e) => back();
            var heading = new Label { Text = title, Font = Theme.Heading(13F), ForeColor = Theme.TextPrimary, AutoSize = true, BackColor = Color.Transparent, Location = new Point(GlassCard.Side + width + 14, 17) };
            Controls.Add(button);
            Controls.Add(heading);
        }
    }

    // Connection state dot: solid for live states, hollow for "not installed". Changes cross-fade the colour
    // and pop 1 → 1.35 → 1; Working and Needs you pulse a ring (900 ms on, 700 ms off); the hollow ring
    // draws itself when it first appears.
    enum DotKind { Connected, Working, Detected, Off, NotInstalled, Error, NeedsYou }

    sealed class StatusDot : Control, IAnimated
    {
        DotKind kind = DotKind.Off;
        Color from = Theme.TextMuted, to = Theme.TextMuted;
        double phase;
        readonly Fade colour = new Fade(150), pop = new Fade(200) { Movement = true }, draw = new Fade(220) { Movement = true };

        public StatusDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(16, 16);
            BackColor = Color.Transparent;
            colour.On = pop.On = draw.On = true;
            colour.T = pop.T = draw.T = 1;
        }

        public DotKind Kind
        {
            get { return kind; }
            set
            {
                if (kind == value) return;
                from = Shown;
                kind = value;
                to = ColorOf(value);
                if (IsHandleCreated && Visible)
                {
                    colour.T = 0; pop.T = 0;
                    if (value == DotKind.NotInstalled) draw.T = 0;
                }
                else { colour.T = 1; from = to; }
                Invalidate();
                Animator.Start(this);
            }
        }

        bool Pulses { get { return kind == DotKind.Working || kind == DotKind.NeedsYou; } }
        Color Shown { get { return Theme.Mix(from, to, colour.T); } }

        public static Color ColorOf(DotKind k)
        {
            switch (k)
            {
                case DotKind.Connected:
                case DotKind.Working: return Theme.Success;
                case DotKind.Detected:
                case DotKind.NeedsYou: return Theme.Warning;
                case DotKind.Error: return Theme.Danger;
                default: return Theme.TextMuted;
            }
        }

        public bool Step(double dt)
        {
            if (IsDisposed) return false;
            bool moving = colour.Step(dt);
            moving |= pop.Step(dt);
            moving |= draw.Step(dt);
            bool pulsing = Pulses && Visible;
            if (pulsing) phase = (phase + dt / 1.6) % 1.0; else phase = 0;
            Invalidate();
            return moving || pulsing;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (Visible && kind == DotKind.NotInstalled) draw.T = 0;   // the ring draws itself each time it's shown
            if (Visible) Animator.Start(this);
            base.OnVisibleChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var c = Shown;
            float cx = Width / 2f, cy = Height / 2f;
            if (Pulses && phase < 0.5625)   // 0.9 s of each 1.6 s cycle
            {
                double q = phase / 0.5625;
                float r = (float)(4 + 5 * Theme.Decel(q));
                using (var p = new Pen(Color.FromArgb((int)(153 * (1 - q)), c), 1.5f)) g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
            }
            float s = pop.T < 1 ? (float)(1 + 0.35 * Math.Sin(Math.PI * pop.T)) : 1;
            if (kind == DotKind.NotInstalled)
            {
                float rr = 3.5f * s;
                float sweep = (float)(360 * Theme.EaseOut(draw.T));
                if (sweep > 1) using (var p = new Pen(c, 1.5f)) g.DrawArc(p, cx - rr, cy - rr, rr * 2, rr * 2, -90, sweep);
            }
            else
            {
                float rr = 4 * s;
                using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
                using (var shine = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillEllipse(shine, cx - rr * 0.55f, cy - rr * 0.8f, rr * 1.1f, rr * 0.7f);
            }
        }
    }

    // An agent's pet playing its idle loop in its own tie (Agents carousel, user 2026-11-01). Switching
    // agents (Gemini Response 5): the new pet slides 40 px in on the Gentle spring, pushing the old one out,
    // while they cross-fade over 250 ms; rapid clicks carry the slide on. Runs only while its page is on
    // screen; the page restarts it with Play().
    sealed class PetPreview : Control, IAnimated
    {
        public bool Dimmed;
        readonly SpriteLibrary library;
        readonly ClipPlayer player = new ClipPlayer();
        TieTint tint;
        string agent;
        bool tinted;       // template clip: the tie takes the agent's colour
        Bitmap frame;      // the current clip frame, premultiplied and tinted
        double clock;      // seconds into the loop
        Bitmap from;       // what was on screen when the agent changed
        double fade = 1;
        int direction;
        const int Travel = 40;
        readonly Spring slide = new Spring(0, Feel.Gentle);   // the new pet's x offset (px)

        public PetPreview(SpriteLibrary library)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            this.library = library;
            BackColor = Color.Transparent;
        }

        // `dir` (-1 or +1) is the side the new pet drifts in from; 0 only refreshes the look (tie, dimming)
        // and keeps the loop playing.
        public void SetAgent(AgentInfo info, int dir)
        {
            if (dir != 0 && IsHandleCreated && Width > 0 && Height > 0 && frame != null)
            {
                var shot = new Bitmap(Width, Height);
                DrawToBitmap(shot, ClientRectangle);   // mid-fade too: the new fade starts from what's on screen
                if (from != null) from.Dispose();
                from = shot;
                fade = 0;
                if (dir != direction) slide.Velocity = 0;   // same way: keep the momentum
                slide.Value = dir * Travel;
                slide.Target = 0;
                direction = dir;
            }
            if (dir != 0 || info.Key != agent || player.Clip == null)
            {
                player.Start(library.Pick(info.Key, Anim.Idle, null, out tinted));
                clock = 0;
            }
            agent = info.Key;
            tint = tinted ? Pet.MakeTint(info, library.Height) : null;
            Render(Math.Max(0, player.Decoded));
            Invalidate();
            Play();
        }

        public void Play() { if (player.Clip != null) Animator.Start(this); }

        public bool Step(double dt)
        {
            if (FindForm() == null || player.Clip == null) return false;
            if (from != null)
            {
                int ms = Theme.Motion(Theme.CrossfadeMs, false);
                fade = ms <= 0 ? 1 : Math.Min(1, fade + dt * 1000 / ms);
                if (fade >= 1) { from.Dispose(); from = null; }
                Invalidate();
            }
            if (slide.Step(dt)) Invalidate();
            clock = (clock + dt) % player.Clip.Duration;
            int f = Math.Min(player.Clip.FrameCount - 1, (int)(clock * player.Clip.Fps));
            if (f != player.Decoded) { Render(f); Invalidate(); }
            return true;
        }

        void Render(int index)
        {
            player.SeekTo(index);
            var clip = player.Clip;
            if (frame == null || frame.Width != clip.Width || frame.Height != clip.Height)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(clip.Width, clip.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            }
            var bits = frame.LockBits(new Rectangle(0, 0, clip.Width, clip.Height), System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(player.Frame, 0, bits.Scan0, player.Frame.Length);   // stride is width × 4 at 32 bpp
                if (tint != null) tint.Apply(bits.Scan0, player);
            }
            finally { frame.UnlockBits(bits); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            double k = from != null ? Theme.Decel(fade) : 1;
            int x = (int)Math.Round(slide.Value);
            if (from != null) Blit(g, from, new Rectangle(x - direction * Travel, 0, Width, Height), 1 - k);
            DrawPet(g, x, k * (Dimmed ? 0.35 : 1));
        }

        // The figure fills the height above a 6 px strip for its soft contact shadow.
        void DrawPet(Graphics g, int dx, double alpha)
        {
            if (frame == null || alpha <= 0.01) return;
            int h = Height - 6, w = (int)Math.Round(frame.Width * h / (float)frame.Height);
            var r = new Rectangle((Width - w) / 2 + dx, 0, w, h);
            float feet = h * 0.988f;   // the clips' shoe line (PetWindow.GroundY)
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shadow = new SolidBrush(Color.FromArgb((int)(alpha * (Theme.Dark ? 90 : 40)), 0, 0, 0)))
                g.FillEllipse(shadow, r.X + w * 0.29f, feet - 3, w * 0.42f, 6);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Blit(g, frame, r, alpha);
        }

        static void Blit(Graphics g, Bitmap bmp, Rectangle dest, double alpha)
        {
            if (alpha <= 0.01) return;
            using (var attrs = new System.Drawing.Imaging.ImageAttributes())
            {
                attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = (float)Math.Min(1, alpha) });
                g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (frame != null) frame.Dispose();
                if (from != null) from.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // A bare chevron (‹ or ›) with no box: brightens on hover, nudges on press (Agents carousel).
    sealed class ChevronButton : FlatControl
    {
        readonly int dir;   // -1 left, +1 right

        public ChevronButton(int direction)
        {
            dir = direction;
            Size = new Size(36, 48);
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Gemini Response 4 (1): 6 × 24 on a 12 × 32 grid, drawn 1.25× here; rest grey, hover blue, press
            // dark blue; nudges 2 px outward on hover. A white emboss sits 1 px below on Light.
            double h = Math.Max(Hover.Eased, Focused && ShowFocusCues ? 1 : 0);
            float cx = Width / 2f + (float)(dir * 2 * Hover.Eased), cy = Height / 2f, w = 7.5f, half = 15;
            var pts = new[] { new PointF(cx - dir * w / 2, cy - half), new PointF(cx + dir * w / 2, cy), new PointF(cx - dir * w / 2, cy + half) };
            if (!Theme.Dark)
                using (var emboss = Icons.RoundPen(Color.White, 2.5f)) g.DrawLines(emboss, pts.Select(q => new PointF(q.X, q.Y + 1.2f)).ToArray());
            Color c = Press.On ? Theme.AccentPressed : Theme.Mix(Theme.Dark ? Theme.TextMuted : Theme.Hex(0x828E9E), Theme.Accent, h);
            using (var pen = Icons.RoundPen(c, 1.9f)) g.DrawLines(pen, pts);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }
    }

    // A list row (Agents, Connections): full-width hover tint and a tie-colour bar on the left that widens
    // 3 → 5 px on hover (Gemini Response 3). Hover counts the whole row, children included.
    class ListRow : Panel, IAnimated
    {
        public Color Tie = Theme.TextMuted;
        public int BarX = 15, BarHeight = 28;
        public bool ShowBar = true;   // Connections rows go without the tie-colour bar
        readonly Fade hover = new Fade(Theme.StandardMs);

        public ListRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = new Padding(0);
        }

        // Very dark ties (Grok) would vanish on the graphite page, so their bar is lifted toward grey.
        public Color BarColor { get { return Tie.GetBrightness() < 0.2f ? Theme.Mix(Tie, Theme.TextSecondary, 0.55) : Tie; } }

        protected override void OnControlAdded(ControlEventArgs e) { Hook(e.Control); base.OnControlAdded(e); }

        void Hook(Control c)
        {
            c.MouseEnter += (s, e) => Track();
            c.MouseLeave += (s, e) => Track();
            foreach (Control child in c.Controls) Hook(child);
        }

        protected override void OnMouseEnter(EventArgs e) { Track(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Track(); base.OnMouseLeave(e); }

        void Track()
        {
            bool inside = IsHandleCreated && ClientRectangle.Contains(PointToClient(Cursor.Position));
            if (inside == hover.On) return;
            hover.On = inside;
            Animator.Start(this);
        }

        public bool Step(double dt)
        {
            if (IsDisposed) return false;
            bool moving = hover.Step(dt);
            Invalidate(true);
            return moving;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (hover.T > 0.01)   // a white sheen over the row
                using (var b = new SolidBrush(Theme.Mix(Color.FromArgb(0, Theme.RowHoverTint), Theme.RowHoverTint, hover.Eased))) using (var p = Theme.Round(new RectangleF(6, 2, Width - 12, Height - 4), 10)) g.FillPath(b, p);
            float w = (float)(3 + 2 * hover.Eased);
            if (ShowBar)
            using (var b = new SolidBrush(BarColor)) using (var p = Theme.Round(new RectangleF(BarX, (Height - BarHeight) / 2f, w, BarHeight), w / 2)) g.FillPath(b, p);
        }
    }

    // A window's first frame drawn from a snapshot, with bands that rise into place on the Smooth spring
    // and fade in over 150 ms, optionally staggered (Welcome rows 60 ms apart; the Settings page settling
    // 12 px). Removes itself when done.
    sealed class Entrance : Control, IAnimated
    {
        sealed class Band { public Rectangle Area; public int Delay, Dy; }
        readonly Bitmap shot;
        readonly List<Band> bands = new List<Band>();
        double ms;
        int total;

        public Entrance(Bitmap shot)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;   // the backdrop shows under bands that haven't arrived yet
            this.shot = shot;
            Bounds = new Rectangle(0, 0, shot.Width, shot.Height);
        }

        public Entrance Add(Rectangle area, int delay, int dy)
        {
            bands.Add(new Band { Area = area, Delay = delay, Dy = dy });
            total = Math.Max(total, delay + (int)(Feel.Smooth.Seconds * 1000));
            return this;
        }

        // `host`'s children as they'll first appear (works before the window is shown).
        public static Bitmap Snapshot(Control host)
        {
            var bmp = new Bitmap(Math.Max(1, host.ClientSize.Width), Math.Max(1, host.ClientSize.Height));
            using (var g = Graphics.FromImage(bmp)) g.Clear(Theme.Canvas);
            for (int i = host.Controls.Count - 1; i >= 0; i--)
            {
                var c = host.Controls[i];
                if (c.Width > 0 && c.Height > 0) c.DrawToBitmap(bmp, c.Bounds);
            }
            return bmp;
        }

        // Cover `host` now (first frame: only the parts outside the bands); Play() starts the motion.
        public void Cover(Control host)
        {
            host.Controls.Add(this);
            BringToFront();
        }

        public void Play() { Animator.Start(this); }

        public bool Step(double dt)
        {
            if (IsDisposed) return false;
            ms += dt * 1000;
            Invalidate();
            if (ms < total) return true;
            if (Parent != null) Parent.Controls.Remove(this);
            Dispose();
            return false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var still = new Region(ClientRectangle))
            {
                foreach (var b in bands) still.Exclude(b.Area);
                g.Clip = still;
                g.DrawImageUnscaled(shot, 0, 0);
                g.ResetClip();
            }
            foreach (var b in bands)
            {
                double k = Feel.Smooth.Progress((ms - b.Delay) / 1000), alpha = Theme.Clamp01((ms - b.Delay) / Theme.StandardMs);
                if (alpha <= 0.01) continue;
                var dest = new Rectangle(b.Area.X, b.Area.Y + (int)Math.Round(b.Dy * (1 - k)), b.Area.Width, b.Area.Height);
                if (alpha >= 0.99) { g.DrawImage(shot, dest, b.Area, GraphicsUnit.Pixel); continue; }
                using (var attrs = new System.Drawing.Imaging.ImageAttributes())
                {
                    attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = (float)alpha });
                    g.DrawImage(shot, dest, b.Area.X, b.Area.Y, b.Area.Width, b.Area.Height, GraphicsUnit.Pixel, attrs);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) shot.Dispose();
            base.Dispose(disposing);
        }
    }
}
