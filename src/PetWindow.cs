using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpaGachi
{
    // An always-on-top layered window showing one pet: name tag on top, sprite below.
    // Pixels go into a DIB section and reach the screen through UpdateLayeredWindow, so there is no
    // WM_PAINT and no GPU work. The sprite area has headroom above the figure (hops, stretch) and a strip
    // below the shoes for the ground decor (contact shadow, Plaza rings, hover glow), drawn with GDI+ into
    // the same DIB under the sprite.
    // Clicks: with Interactive on, only visible pixels catch the mouse (a layered window lets clicks
    // through wherever alpha is 0), and the window never takes focus; with it off the whole window is
    // click-through.
    sealed class PetWindow : Form
    {
        public const int TagHeight = 20;

        public int AreaWidth { get; private set; }    // sprite area (window minus the name tag strip)
        public int AreaHeight { get; private set; }
        public int ShoeY { get; private set; }        // where the clip's bottom edge sits in the sprite area
        int width, height, figureHeight, footPad;
        IntPtr memDc, bitmap, oldBitmap, bits;
        Bitmap areaBitmap;      // GDI+ view of the sprite area, for the ground decor
        Graphics areaGraphics;
        byte[] fadeFrom;      // last shown sprite area, for cross-fading into a new clip
        int fadeFrames, fadeTotal;
        byte[] tinted = new byte[0];   // the clip frame with the agent's tie applied, before scaling
        readonly Scaler spriteScaler = new Scaler();
        string name = "";

        public float BadgeScale = 1f;   // pet size factor: badge and glow sizes are for Normal

        bool interactive;
        string tagShown = "";
        public bool Hovered { get; private set; }
        public event EventHandler PetClicked, HoverChanged, PetPressed, PetReleased;
        string nameStyle = NameFill.White;

        // The name's fill, a colour or a gradient preset (Settings → Agents → Name colour); the outline stays black
        // so any of them reads.
        public string NameStyle
        {
            get { return nameStyle; }
            set { if (value == nameStyle) return; nameStyle = value; tagShown = ""; if (name.Length > 0) SetName(name); }
        }
        public event EventHandler<MouseEventArgs> PetMenu;

        public PetWindow(int areaWidth, int areaHeight, bool interactive)
        {
            this.interactive = interactive;
            Cursor = Cursors.Hand;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Configure(areaWidth, areaHeight, 0, 0);
        }

        // Resizes the drawing surface (pet size or standing boost changed). Cheap when unchanged.
        public void Configure(int areaWidth, int figureAreaHeight, int topPad, int bottomPad)
        {
            int areaHeight = topPad + figureAreaHeight + bottomPad;
            if (areaWidth == AreaWidth && areaHeight == AreaHeight && ShoeY == topPad + figureAreaHeight && memDc != IntPtr.Zero) return;
            FreeSurface();
            AreaWidth = areaWidth;
            AreaHeight = areaHeight;
            ShoeY = topPad + figureAreaHeight;
            figureHeight = figureAreaHeight;
            footPad = bottomPad;
            width = areaWidth;
            height = areaHeight + TagHeight;
            Size = new Size(width, height);
            CreateSurface();
            fadeFrames = 0;
            tagShown = "";
            if (name.Length > 0) SetName(name);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                if (!interactive) cp.ExStyle |= Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void SetInteractive(bool on)
        {
            if (on == interactive) return;
            interactive = on;
            if (!IsHandleCreated) return;
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, on ? ex & ~Native.WS_EX_TRANSPARENT : ex | Native.WS_EX_TRANSPARENT);
            if (!on && Hovered) { Hovered = false; if (HoverChanged != null) HoverChanged(this, EventArgs.Empty); }
        }

        const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }   // never steal focus
            base.WndProc(ref m);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            Hovered = true;
            if (HoverChanged != null) HoverChanged(this, EventArgs.Empty);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Hovered = false;
            if (HoverChanged != null) HoverChanged(this, EventArgs.Empty);
            if (PetReleased != null) PetReleased(this, EventArgs.Empty);
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && PetPressed != null) PetPressed(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && PetReleased != null) PetReleased(this, EventArgs.Empty);
            if (e.Button == MouseButtons.Left && PetClicked != null) PetClicked(this, EventArgs.Empty);   // the name tag too: no usage bubble (user, 2026-11-01)
            if (e.Button == MouseButtons.Right && PetMenu != null) PetMenu(this, e);
            base.OnMouseUp(e);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        void CreateSurface()
        {
            var bmi = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)),
                biWidth = width,
                biHeight = -height,   // top-down rows
                biPlanes = 1,
                biBitCount = 32,
            };
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            memDc = Native.CreateCompatibleDC(screen);
            bitmap = Native.CreateDIBSection(screen, ref bmi, 0, out bits, IntPtr.Zero, 0);
            Native.ReleaseDC(IntPtr.Zero, screen);
            if (bitmap == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
            oldBitmap = Native.SelectObject(memDc, bitmap);
            fadeFrom = new byte[AreaWidth * AreaHeight * 4];
            areaBitmap = new Bitmap(AreaWidth, AreaHeight, AreaWidth * 4, PixelFormat.Format32bppPArgb, SpriteBits);
            areaGraphics = Graphics.FromImage(areaBitmap);
            areaGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        }

        void FreeSurface()
        {
            if (memDc == IntPtr.Zero) return;
            areaGraphics.Dispose();
            areaBitmap.Dispose();
            Native.SelectObject(memDc, oldBitmap);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memDc);
            memDc = IntPtr.Zero;
        }

        IntPtr SpriteBits { get { return bits + TagHeight * width * 4; } }

        public void SetName(string text)
        {
            name = text;
            tagShown = "";
            ShowTag(text, "", 0);
        }

        // The name strip: the name alone, or while hovered a pill that grows over 200 ms into
        // "Name · what it's doing" (Gemini Response 3): Surface fill, teal edge, a 10% gloss band; the state
        // fades in from +50 ms. `grow` is 0..1, quantised so a hover re-renders at most ~10 times.
        public void ShowTag(string text, string state, double grow)
        {
            int step = (int)Math.Round(Math.Max(0, Math.Min(1, grow)) * 10);
            string key = text + "\n" + (step > 0 ? state : "") + "\n" + step;
            if (key == tagShown && memDc != IntPtr.Zero) return;
            tagShown = key;
            var tag = RenderTag(text, state, step / 10.0, width, TagHeight, nameStyle);
            Marshal.Copy(tag, 0, bits, tag.Length);
        }

        // Snapshot what is on screen so the next few frames blend from it.
        public void BeginFade(int frames)
        {
            Marshal.Copy(SpriteBits, fadeFrom, 0, fadeFrom.Length);
            fadeFrames = fadeTotal = frames;
        }

        // Ground decor under the figure, then the tinted clip frame scaled by `scale` × the pose's squash
        // (anchored bottom centre, so the feet stay on the ground) and offset by the pose, a cross-fade if a
        // clip just changed, then the status badge next to the head.
        public unsafe void DrawFrame(ClipPlayer player, TieTint tint, float scale, Pose pose)
        {
            var clip = player.Clip;
            byte[] frame = player.Frame;
            if (tinted.Length != frame.Length) tinted = new byte[frame.Length];
            Buffer.BlockCopy(frame, 0, tinted, 0, frame.Length);
            int w = Math.Max(1, (int)Math.Round(clip.Width * scale * pose.Sx)), h = Math.Max(1, (int)Math.Round(clip.Height * scale * pose.Sy));
            int x0 = (AreaWidth - w) / 2 + (int)Math.Round(pose.Dx), y0 = ShoeY - h + (int)Math.Round(pose.Dy);
            byte* area = (byte*)SpriteBits;
            Clear(area, AreaWidth * AreaHeight * 4);
            DrawGround(pose, clip.Height * scale);
            fixed (byte* src = tinted)
            {
                if (tint != null) tint.Apply((IntPtr)src, player);
                spriteScaler.Draw(src, clip.Width, clip.Height, area, AreaWidth, AreaHeight, x0, y0, w, h, true);
            }
            if (fadeFrames > 0)
            {
                int t = 255 * (fadeTotal - fadeFrames + 1) / (fadeTotal + 1);
                Blend(SpriteBits, fadeFrom, t);
                fadeFrames--;
            }
            var box = new RectangleF(x0, y0, w, h);
            if (pose.Fx != null) pose.Fx.Draw(areaGraphics, box);
            if (pose.Beacon >= 0)
            {
                float size = Math.Max(16, 24 * BadgeScale);
                Effects.Beacon(areaGraphics, box, pose.Beacon, x0 + w * 0.83f + size * 0.35f, Math.Max(0, y0 + h * 0.025f) + size * 0.2f, size);
            }
            if (pose.Badge != null && pose.BadgeAlpha > 0.01) DrawBadge(pose, x0, y0, w, h);
        }

        // The badge sits right of the head (the same spot as on the original 147×160 canvas), 24 px at
        // Normal. It scales from its bottom-left corner (toward the head) and wiggles about its centre.
        void DrawBadge(Pose pose, int x0, int y0, int w, int h)
        {
            var g = areaGraphics;
            float size = Math.Max(16, 24 * BadgeScale);
            float bx = x0 + w * 0.83f - size / 2 + pose.BadgeDx, by = Math.Max(0, y0 + h * 0.025f) + pose.BadgeDy;
            var state = g.Save();
            g.TranslateTransform(bx, by + size);
            g.ScaleTransform(pose.BadgeScale * pose.BadgeSx, pose.BadgeScale * pose.BadgeSy);
            g.TranslateTransform(0, -size);
            if (pose.BadgeAngle != 0)
            {
                g.TranslateTransform(size / 2, size / 2);
                g.RotateTransform(pose.BadgeAngle);
                g.TranslateTransform(-size / 2, -size / 2);
            }
            g.ScaleTransform(size / 24f, size / 24f);
            BadgeArt.Draw(g, pose);
            g.Restore(state);
        }

        // The ground line sits a touch above the clip's bottom edge (the shoes have a little margin).
        float GroundY(float figure) { return ShoeY - figure * 0.012f; }

        void DrawGround(Pose pose, float figure)
        {
            var g = areaGraphics;
            float cx = AreaWidth / 2f, gy = GroundY(figure);
            if (pose.ShadowAlpha > 0 && pose.ShadowWidth > 1)
            {
                float sw = pose.ShadowWidth, sh = Math.Max(3, sw * 8 / 44);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(cx - sw / 2, gy - sh / 2, sw, sh);
                    using (var brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = Color.FromArgb((int)(255 * pose.ShadowAlpha), Theme.PetShadow);
                        brush.SurroundColors = new[] { Color.FromArgb(0, Theme.PetShadow) };
                        g.FillPath(brush, path);
                    }
                }
            }
            if (pose.Glow > 0.01)
            {
                float gw = 48 * BadgeScale, gh = 12 * BadgeScale;
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(cx - gw / 2, gy - gh / 2, gw, gh);
                    using (var brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = Color.FromArgb((int)(255 * pose.Glow), pose.GlowTint);
                        brush.SurroundColors = new[] { Color.FromArgb(0, pose.GlowTint) };
                        g.FillPath(brush, path);
                    }
                }
            }
            foreach (var r in pose.Rings)
            {
                if (r.Alpha <= 0.01 || r.Width < 1) continue;
                float rh = r.Width * 0.3f;
                using (var pen = new Pen(Color.FromArgb((int)(255 * Math.Min(1, r.Alpha)), r.Color), Math.Max(1, r.Stroke)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    if (r.Sweep > 0) g.DrawArc(pen, cx - r.Width / 2, gy - rh / 2, r.Width, rh, r.Start, r.Sweep);
                    else g.DrawEllipse(pen, cx - r.Width / 2, gy - rh / 2, r.Width, rh);
                }
            }
        }

        static unsafe void Clear(byte* p, int bytes)
        {
            ulong* q = (ulong*)p;
            int words = bytes / 8;
            for (int i = 0; i < words; i++) q[i] = 0;
            for (int i = words * 8; i < bytes; i++) p[i] = 0;
        }

        static unsafe void Blend(IntPtr dst, byte[] from, int t)
        {
            byte* d = (byte*)dst;
            int inv = 255 - t;
            for (int i = 0; i < from.Length; i++) d[i] = (byte)((d[i] * t + from[i] * inv) / 255);
        }

        public void Present(int x, int y, byte opacity)
        {
            var dst = new Native.POINT(x, y);
            var size = new Native.SIZE(width, height);
            var src = new Native.POINT(0, 0);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                SourceConstantAlpha = opacity,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }

        // Dev aid (POST /signal {"snapshot": path}): the window's current pixels as a PNG with alpha, so the
        // choreography can be checked even when other windows cover the pets.
        public void Snapshot(string path)
        {
            using (var view = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits))
            using (var copy = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(copy)) g.DrawImageUnscaled(view, 0, 0);
                copy.Save(path, ImageFormat.Png);
            }
        }

        public void KeepOnTop()
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // "Desktop only": not topmost, sitting directly above the desktop so every app window covers it.
        public void KeepOnDesktop(IntPtr desktop)
        {
            IntPtr above = desktop == IntPtr.Zero ? IntPtr.Zero : Native.GetWindow(desktop, Native.GW_HWNDPREV);
            if (above == Handle) return;   // already right above the desktop
            IntPtr after = above == IntPtr.Zero || (Native.GetWindowLong(above, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0
                ? Native.HWND_NOTOPMOST : above;
            Native.SetWindowPos(Handle, after, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // The name in its colour with a thin black outline, the same in both themes (user); while hovered the pill grows around "Name · state", the name
        // gliding left as the state fades in after it (premultiplied BGRA, rendered once per step). Text too wide for the
        // pet shrinks to fit instead of being cut off (user, 2026-11-01).
        static byte[] RenderTag(string text, string state, double k, int w, int h, string nameStyle)
        {
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var namePath = new GraphicsPath())
                using (var statePath = new GraphicsPath())
                using (var font = Theme.Heading(9F))
                using (var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap })
                using (var outline = new Pen(Color.FromArgb(230, 0, 0, 0), 2f) { LineJoin = LineJoin.Round })
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    var family = font.FontFamily;
                    var style = (int)font.Style;
                    namePath.AddString(text, family, style, 13f, PointF.Empty, format);
                    bool withState = k > 0 && !string.IsNullOrEmpty(state);
                    if (withState) statePath.AddString("· " + state, family, (int)FontStyle.Regular, 12f, PointF.Empty, format);
                    float gap = 5;
                    var nb = namePath.GetBounds();
                    RectangleF sb = withState ? statePath.GetBounds() : RectangleF.Empty;
                    float room = w - (withState ? 20 : 6), need = withState ? nb.Width + gap + sb.Width : nb.Width;
                    if (need > room && need > 0)
                    {
                        float fit = room / need;
                        using (var m = new Matrix()) { m.Scale(fit, fit); namePath.Transform(m); if (withState) statePath.Transform(m); }
                        gap *= fit;
                        nb = namePath.GetBounds();
                        if (withState) sb = statePath.GetBounds();
                    }
                    float nameW = nb.Width, fullW = withState ? nb.Width + gap + sb.Width : nb.Width;
                    float startRest = (w - nameW) / 2, startFull = (w - fullW) / 2;
                    float ease = (float)Theme.Decel(k);
                    float nameX = startRest + (startFull - startRest) * ease;
                    float midY = h / 2f;
                    if (k > 0)
                    {
                        float pw = Math.Min(w - 2, nameW + 16 + (fullW - nameW) * ease), px = (w - pw) / 2;
                        var shape = new RectangleF(px, 1, pw, h - 2);
                        using (var bg = new SolidBrush(Color.FromArgb((int)(200 * ease), Theme.PetShadow)))
                        using (var edge = new Pen(Color.FromArgb((int)(255 * ease), Theme.Accent)))
                        using (var path = Theme.Round(shape, (h - 2) / 2f))
                        {
                            g.FillPath(bg, path);
                            using (var gloss = new LinearGradientBrush(new RectangleF(px, 0.5f, pw, h / 2f + 1), Color.FromArgb((int)(26 * ease), 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                            {
                                var old = g.Clip;
                                g.SetClip(path, CombineMode.Intersect);
                                g.FillRectangle(gloss, px, 1, pw, h / 2f);
                                g.Clip = old;
                            }
                            g.DrawPath(edge, path);
                        }
                    }
                    using (var m = new Matrix())
                    {
                        m.Translate(nameX - nb.X, midY - nb.Y - nb.Height / 2);
                        namePath.Transform(m);
                    }
                    g.DrawPath(outline, namePath);
                    using (var fill = NameFill.Brush(nameStyle, namePath.GetBounds())) g.FillPath(fill, namePath);
                    if (withState)
                    {
                        double fadeIn = Math.Max(0, Math.Min(1, (k * 200 - 50) / 140));
                        if (fadeIn > 0)
                        {
                            using (var m = new Matrix())
                            {
                                m.Translate(nameX + nameW + gap - sb.X, midY - sb.Y - sb.Height / 2);
                                statePath.Transform(m);
                            }
                            using (var thin = new Pen(Color.FromArgb((int)(230 * fadeIn), 0, 0, 0), 2f) { LineJoin = LineJoin.Round }) g.DrawPath(thin, statePath);
                            using (var fill = new SolidBrush(Color.FromArgb((int)(255 * fadeIn), 0xD8, 0xDC, 0xE2))) g.FillPath(fill, statePath);
                        }
                    }
                }
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                var bytes = new byte[w * h * 4];
                for (int row = 0; row < h; row++) Marshal.Copy(data.Scan0 + row * data.Stride, bytes, row * w * 4, w * 4);
                bmp.UnlockBits(data);
                return bytes;
            }
        }

        protected override void Dispose(bool disposing)
        {
            FreeSurface();
            base.Dispose(disposing);
        }
    }
}
