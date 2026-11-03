using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpaGachi
{
    // Experimental custom pet location (user, 2026-11-02). Covers the chosen display with a view of the bare desktop
    // (wallpaper and icons, no windows: Windows renders the desktop window on request) and shows the pets standing
    // on a dotted line. Drag the line (or nudge it with the arrow keys) to where the pets should stand; Save keeps
    // the row's centre and ground line as fractions of the display's work area, so it survives resolution changes.
    sealed class PlacementForm : Form
    {
        readonly Screen screen;
        readonly Bitmap desk, pet;
        readonly int step, count;
        readonly FlatButton save = new FlatButton("Save", 96) { Primary = true }, cancel = new FlatButton("Cancel", 96);
        PointF spot;            // the row's centre on its ground line, in window coordinates
        bool dragging;
        PointF grabOffset;
        public double X { get; private set; }   // the result, as fractions of the work area
        public double Y { get; private set; }

        public PlacementForm(Screen screen, Bitmap pet, int step, int count, double x, double y)
        {
            this.screen = screen;
            this.pet = pet;
            this.step = step;
            this.count = count;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Text = "OpaGachi · Pet location";
            Bounds = screen.Bounds;
            BackColor = Color.Black;
            Cursor = Cursors.SizeAll;
            desk = DesktopView(screen.Bounds);
            var work = screen.WorkingArea;
            spot = new PointF(work.Left - screen.Bounds.Left + (float)(x * work.Width), work.Top - screen.Bounds.Top + (float)(y * work.Height));
            save.Click += (s, e) => Finish(true);
            cancel.Click += (s, e) => Finish(false);
            save.Cursor = cancel.Cursor = Cursors.Hand;
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
        }

        // The desktop window renders just the wallpaper and icons, even with apps open on top.
        static Bitmap DesktopView(Rectangle bounds)
        {
            var bmp = new Bitmap(bounds.Width, bounds.Height);
            try
            {
                IntPtr progman = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
                Native.RECT r;
                if (progman != IntPtr.Zero && Native.GetWindowRect(progman, out r))
                {
                    using (var whole = new Bitmap(Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top)))
                    {
                        using (var g = Graphics.FromImage(whole))
                        {
                            IntPtr hdc = g.GetHdc();
                            try { PrintWindow(progman, hdc, 2 /* PW_RENDERFULLCONTENT */); }
                            finally { g.ReleaseHdc(hdc); }
                        }
                        using (var g = Graphics.FromImage(bmp))
                            g.DrawImage(whole, new Rectangle(0, 0, bounds.Width, bounds.Height), new Rectangle(bounds.X - r.Left, bounds.Y - r.Top, bounds.Width, bounds.Height), GraphicsUnit.Pixel);
                    }
                }
            }
            catch (Exception e) { Log.Write("placement: desktop view: " + e.Message); }
            return bmp;
        }

        int RowWidth { get { return pet.Width + (count - 1) * step; } }

        RectangleF Row
        {
            get { float w = RowWidth + 48; return new RectangleF(spot.X - w / 2, spot.Y - pet.Height - 34, w, pet.Height + 46); }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            save.Location = new Point(Width / 2 + 6, 70);
            cancel.Location = new Point(Width / 2 - 6 - cancel.Width, 70);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (!Row.Contains(e.Location)) spot = new PointF(e.X, e.Y + 10);   // a click elsewhere brings the row there
            dragging = true;
            grabOffset = new PointF(spot.X - e.X, spot.Y - e.Y);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            spot = Clamp(new PointF(e.X + grabOffset.X, e.Y + grabOffset.Y));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int n = e.Shift ? 20 : 2;
            if (e.KeyCode == Keys.Left) spot.X -= n;
            else if (e.KeyCode == Keys.Right) spot.X += n;
            else if (e.KeyCode == Keys.Up) spot.Y -= n;
            else if (e.KeyCode == Keys.Down) spot.Y += n;
            else { base.OnKeyDown(e); return; }
            spot = Clamp(spot);
            e.Handled = true;
            Invalidate();
        }

        // The whole row and its name tags stay on the display.
        PointF Clamp(PointF p)
        {
            float half = RowWidth / 2f;
            return new PointF(Math.Max(half, Math.Min(Width - half, p.X)), Math.Max(pet.Height + 24, Math.Min(Height, p.Y)));
        }

        void Finish(bool keep)
        {
            if (keep)
            {
                var work = screen.WorkingArea;
                X = Theme.Clamp01((screen.Bounds.Left + spot.X - work.Left) / work.Width);
                Y = Theme.Clamp01((screen.Bounds.Top + spot.Y - work.Top) / work.Height);
            }
            DialogResult = keep ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(desk, 0, 0);
            using (var dim = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(dim, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Instructions in a glass capsule above the buttons.
            const string hint = "Drag the pets to where they should stand";
            using (var f = Theme.Semibold(11F))
            {
                var size = TextRenderer.MeasureText(hint, f);
                var pill = new RectangleF(Width / 2f - size.Width / 2f - 22, 22, size.Width + 44, 38);
                Theme.Glass(g, pill, pill.Height / 2);
                TextRenderer.DrawText(g, hint, f, Rectangle.Round(pill), Theme.TextPrimary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // The row: a soft plate, the pets on their shadows, and the dotted ground line through their feet.
            var row = Row;
            using (var plate = new SolidBrush(Color.FromArgb(dragging ? 70 : 45, 255, 255, 255))) using (var p = Theme.Round(row, 16)) g.FillPath(plate, p);
            float left = spot.X - RowWidth / 2f;
            for (int i = 0; i < count; i++)
            {
                float x = left + i * step;
                using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillEllipse(shadow, x + pet.Width * 0.29f, spot.Y - 4, pet.Width * 0.42f, 8);
                g.DrawImage(pet, x, spot.Y - pet.Height, pet.Width, pet.Height);
            }
            using (var halo = new Pen(Color.FromArgb(150, 0, 0, 0), 4f)) g.DrawLine(halo, row.Left + 10, spot.Y, row.Right - 10, spot.Y);
            using (var line = new Pen(Color.White, 2f) { DashStyle = DashStyle.Dash }) g.DrawLine(line, row.Left + 10, spot.Y, row.Right - 10, spot.Y);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { desk.Dispose(); pet.Dispose(); }
            base.Dispose(disposing);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    }
}
