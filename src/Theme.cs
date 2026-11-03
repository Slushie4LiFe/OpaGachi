using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpaGachi
{
    // Design tokens and helpers. Since 2026-11-01 the look is "liquid glass" (user): a soft backdrop with
    // three colour pools, frosted glass cards and capsule controls on top, the cherry-red gel accent kept,
    // and Inter for every face. Light (default) and Dark share token names; High contrast swaps the glass
    // for solid surfaces and strong edges. Translucent tokens (Border, Raised*, Track*, Glass*) carry alpha:
    // use them for drawing, never as a control's BackColor. Apply() swaps them; open windows are rebuilt.
    static class Theme
    {
        public static bool Dark, HighContrast;
        public static float TextScale = 1f;   // accessibility: every font size is multiplied by this

        // Opaque surfaces: canvas (the backdrop's average, for colour fades), surface (glass over the
        // backdrop, ditto), elevated (menus, tooltips), well (text boxes), hover (menu items).
        public static Color Canvas, Well, Surface, Elevated, Hover;
        public static Color Border, BorderStrong, BorderHover;
        public static Color TextPrimary, TextSecondary, TextMuted;
        public static Color Accent, AccentHover, AccentPressed, AccentText;
        public static Color Success, Warning, Danger;

        // Gel accent (primary buttons, on toggles, slider fill, selected tab) and the glass finish for the rest.
        public static Color GelTop, GelBottom, GelPressed, RaisedTop, RaisedBottom, TrackTop, TrackBottom, DropShadow, GaugeTrack;
        public static int Rim;
        public static Color GlassFill, GlassEdgeTop, GlassEdgeBottom, GlassHairline;
        public static int GlassSheen, GlassShadow;
        static Color backdropTop, backdropBottom, poolA, poolB, poolC;

        // Usage fullness: green < 50%, yellow < 75%, orange < 90%, red.
        public static Color LevelGreen, LevelYellow, LevelOrange, LevelRed;

        // Tie identity accents (Gemini Response 3, 4.10): swatches, rings and effects only.
        public static readonly Color TieClaude = Hex(0xD97757);
        public static readonly Color TieChatGpt = Hex(0x5B2BA0);
        public static readonly Color TieGemini = Hex(0xF3D9E5);
        public static readonly Color TieGrok = Hex(0x111114);

        public static Color WellInnerShadow;
        public static readonly Color PetShadow = Hex(0x090A0D);   // ground shadow under the pets: dark on any desktop
        public const int GlossTopRest = 24, GlossTopHover = 36, GlossBottom = 4;   // white alpha
        public static Color RowHoverTint, StaleArc;

        static Theme() { Apply(false, false, 1f); }

        public static void Apply(bool dark, bool contrast, float textScale)
        {
            Dark = dark;
            HighContrast = contrast;
            TextScale = Math.Max(0.8f, Math.Min(1.4f, textScale));
            GelTop = Hex(0xFF5C73); GelBottom = Hex(0xD1052B); GelPressed = Hex(0xA0001F);
            if (!dark)
            {
                Canvas = Hex(0xEEF0F5); Well = Hex(0xEDEEF2); Surface = Hex(0xF7F8FA); Elevated = Hex(0xFFFFFF); Hover = Hex(0xF5E6EA);
                Border = A(26, 0x000000); BorderStrong = A(56, 0x000000); BorderHover = A(92, 0x000000);
                TextPrimary = Hex(0x1D1D1F); TextSecondary = Hex(0x6E6E73); TextMuted = Hex(0x9A9AA0);
                Accent = Hex(0xE0123F); AccentHover = Hex(0xEC3558); AccentPressed = Hex(0xB30E33); AccentText = Hex(0xFFFFFF);
                Success = Hex(0x30B850); Warning = Hex(0xFF9500); Danger = Hex(0xFF3B30);
                RaisedTop = A(245, 0xFFFFFF); RaisedBottom = A(190, 0xFFFFFF); TrackTop = A(44, 0x000000); TrackBottom = A(20, 0x000000);
                DropShadow = A(30, 0x000000); GaugeTrack = A(26, 0x000000); Rim = 210;
                GlassFill = A(150, 0xFFFFFF); GlassEdgeTop = A(250, 0xFFFFFF); GlassEdgeBottom = A(150, 0xFFFFFF); GlassHairline = A(46, 0x1E2A44);   // cards stand out on the pale backdrop (user, 2026-11-02)
                GlassSheen = 110; GlassShadow = 30;
                backdropTop = Hex(0xF6F7FB); backdropBottom = Hex(0xE9ECF3);
                poolA = A(78, 0xFF5C73); poolB = A(60, 0x8C78FF); poolC = A(60, 0x5AC8FA);
                LevelGreen = Hex(0x34C759); LevelYellow = Hex(0xFFCC00); LevelOrange = Hex(0xFF9500); LevelRed = Hex(0xFF3B30);
                WellInnerShadow = A(40, 0x000000);
                RowHoverTint = A(110, 0xFFFFFF); StaleArc = Hex(0xC7A3AB);
            }
            else
            {
                Canvas = Hex(0x15161C); Well = Hex(0x17181D); Surface = Hex(0x202128); Elevated = Hex(0x26282F); Hover = Hex(0x33353E);
                Border = A(30, 0xFFFFFF); BorderStrong = A(56, 0xFFFFFF); BorderHover = A(90, 0xFFFFFF);
                TextPrimary = Hex(0xF5F5F7); TextSecondary = Hex(0xA1A1A6); TextMuted = Hex(0x6E6E75);
                Accent = Hex(0xFF4D6A); AccentHover = Hex(0xFF6B83); AccentPressed = Hex(0xD62A4A); AccentText = Hex(0xFFFFFF);
                Success = Hex(0x34C77B); Warning = Hex(0xE5A035); Danger = Hex(0xE55353);
                RaisedTop = A(46, 0xFFFFFF); RaisedBottom = A(18, 0xFFFFFF); TrackTop = A(70, 0x000000); TrackBottom = A(30, 0xFFFFFF);
                DropShadow = A(90, 0x000000); GaugeTrack = A(34, 0xFFFFFF); Rim = 40;
                GlassFill = A(20, 0xFFFFFF); GlassEdgeTop = A(70, 0xFFFFFF); GlassEdgeBottom = A(16, 0xFFFFFF); GlassHairline = A(110, 0x000000);
                GlassSheen = 22; GlassShadow = 44;
                backdropTop = Hex(0x15161B); backdropBottom = Hex(0x0E0F13);
                poolA = A(24, 0xD61E46); poolB = A(18, 0x6450DC); poolC = A(16, 0x288CC8);   // very subtle, like Light (user, 2026-11-02)
                LevelGreen = Hex(0x34C77B); LevelYellow = Hex(0xE5C84A); LevelOrange = Hex(0xE8833A); LevelRed = Hex(0xE55353);
                WellInnerShadow = A(120, 0x000000);
                RowHoverTint = A(16, 0xFFFFFF); StaleArc = Hex(0x93505E);
            }
            if (contrast)
            {
                // Solid surfaces, full-strength text and edges, a deeper accent (4.5:1 on white).
                Color ink = dark ? Color.White : Color.Black;
                Canvas = Surface = dark ? Color.Black : Color.White;
                backdropTop = backdropBottom = Canvas;
                TextPrimary = ink; TextSecondary = dark ? Hex(0xE5E5EA) : Hex(0x2C2C2E); TextMuted = dark ? Hex(0xC7C7CC) : Hex(0x48484A);
                Border = Color.FromArgb(150, ink); BorderStrong = BorderHover = ink;
                Accent = dark ? Hex(0xFF7088) : Hex(0xB0002A); AccentPressed = dark ? Hex(0xFF8FA2) : Hex(0x8A0021);
                GelTop = dark ? Hex(0xFF7088) : Hex(0xC4002F); GelBottom = GelPressed = Accent;
                GlassFill = Surface; GlassEdgeTop = GlassEdgeBottom = ink; GlassHairline = Color.Transparent;
                GlassSheen = 0; GlassShadow = 0;
                RaisedTop = RaisedBottom = Surface; TrackTop = TrackBottom = dark ? Hex(0x3A3A3C) : Hex(0xD1D1D6);
                GaugeTrack = dark ? Hex(0x48484A) : Hex(0xC7C7CC); Rim = 0;
                RowHoverTint = dark ? Hex(0x2C2C2E) : Hex(0xE5E5EA);
            }
            backdrop = pageBackdrop = null;
        }

        static Color A(int alpha, int rgb) { return Color.FromArgb(alpha, Hex(rgb)); }

        // A step away from `c` toward more contrast: lighter on Dark, darker on Light.
        public static Color Lift(Color c, double amount) { return Mix(c, Dark ? Color.White : Color.Black, amount); }

        // ---- Glass -----------------------------------------------------------------------------

        static Bitmap backdrop, pageBackdrop;

        // The window backdrop: a vertical wash with three soft colour pools (cherry top left, violet right,
        // sky bottom). Forms paint it in OnPaintBackground; see-through children show it through.
        public static void Backdrop(Graphics g, Size client, Rectangle clip)
        {
            Backdrop(g, client, clip, Point.Empty, client.Height);
        }

        // Part of the backdrop: window pixel (dest + offset) lands at dest. A scrolling page passes its place in the
        // window plus its scroll, so its backdrop moves with the content and scrolling just blits (user, 2026-11-02:
        // repainting everything on each scroll step lagged and tore). `height` is how far down the page reaches.
        public static void Backdrop(Graphics g, Size window, Rectangle dest, Point offset, int height)
        {
            if (window.Width <= 0 || window.Height <= 0 || dest.Width <= 0 || dest.Height <= 0) return;
            bool page = height > window.Height;
            var bmp = page ? pageBackdrop : backdrop;
            if (bmp == null || bmp.Width != window.Width || bmp.Height < height || (!page && bmp.Height != window.Height) || bmp.Tag == null || (int)bmp.Tag != window.Height)
            {
                if (bmp != null) bmp.Dispose();
                bmp = MakeBackdrop(window.Width, page ? height + 400 : window.Height, window.Height);   // a page gets room to grow
                if (page) pageBackdrop = bmp; else backdrop = bmp;
            }
            g.DrawImage(bmp, dest, new Rectangle(dest.X + offset.X, dest.Y + offset.Y, dest.Width, dest.Height), GraphicsUnit.Pixel);
        }

        // The wash runs over the window's height `gh`; a taller page continues in the bottom colour.
        static Bitmap MakeBackdrop(int w, int h, int gh)
        {
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb) { Tag = gh };
            using (var g = Graphics.FromImage(bmp))
            {
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, w, gh + 1), backdropTop, backdropBottom, 90f)) g.FillRectangle(b, 0, 0, w, gh);
                if (h > gh) using (var b = new SolidBrush(backdropBottom)) g.FillRectangle(b, 0, gh, w, h - gh);
                if (!HighContrast)
                {
                    Pool(g, w * 0.10f, gh * 0.02f, w * 0.62f, poolA);
                    Pool(g, w * 1.00f, gh * 0.42f, w * 0.58f, poolB);
                    Pool(g, w * 0.32f, gh * 1.04f, w * 0.62f, poolC);
                }
            }
            return bmp;
        }

        static void Pool(Graphics g, float cx, float cy, float r, Color c)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                using (var b = new PathGradientBrush(path) { CenterColor = c, SurroundColors = new[] { Color.FromArgb(0, c) } })
                {
                    b.Blend = new Blend { Factors = new[] { 0f, 0.18f, 0.55f, 1f }, Positions = new[] { 0f, 0.35f, 0.7f, 1f } };   // a soft, blurred edge
                    g.FillPath(b, path);
                }
            }
        }

        // A frosted glass panel: soft shadow, translucent white body, a sheen over the top half and a bright
        // rim that fades toward the bottom.
        public static void Glass(Graphics g, RectangleF r, float radius)
        {
            var mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (GlassShadow > 0)
            {
                using (var b = new SolidBrush(Color.FromArgb(GlassShadow / 2, 0, 0, 0))) using (var p = Round(new RectangleF(r.X - 1, r.Y + 1, r.Width + 2, r.Height + 2), radius + 1)) g.FillPath(b, p);
                using (var b = new SolidBrush(Color.FromArgb(GlassShadow, 0, 0, 0))) using (var p = Round(new RectangleF(r.X, r.Y + 1, r.Width, r.Height), radius)) g.FillPath(b, p);
            }
            using (var shape = Round(r, radius))
            {
                using (var b = new SolidBrush(GlassFill)) g.FillPath(b, shape);
                if (GlassSheen > 0)
                {
                    var band = new RectangleF(r.X, r.Y, r.Width, Math.Max(2, r.Height * 0.5f));
                    using (var b = new LinearGradientBrush(new RectangleF(band.X, band.Y - 0.5f, band.Width, band.Height + 1), Color.FromArgb(GlassSheen, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    {
                        var old = g.Clip;
                        g.SetClip(shape, CombineMode.Intersect);
                        g.FillRectangle(b, band);
                        g.Clip = old;
                    }
                }
            }
            var edge = RectangleF.Inflate(r, -0.5f, -0.5f);
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), GlassEdgeTop, GlassEdgeBottom, 90f))
            using (var pen = new Pen(b, HighContrast ? 1.5f : 1f))
            using (var p = Round(edge, radius - 0.5f)) g.DrawPath(pen, p);
            if (GlassHairline.A > 0) using (var pen = new Pen(GlassHairline)) using (var p = Round(RectangleF.Inflate(r, 0.5f, 0.5f), radius + 0.5f)) g.DrawPath(pen, p);
            g.SmoothingMode = mode;
        }

        // Windows 11 rounds the corners of a borderless window or menu (no-op elsewhere).
        public static void RoundCorners(IntPtr hwnd)
        {
            int round = 2;   // DWMWCP_ROUND
            try { DwmSetWindowAttribute(hwnd, 33, ref round, 4); } catch (Exception) { }
        }

        // Fill `path` (bounds `r`) with a vertical gradient.
        public static void Gradient(Graphics g, GraphicsPath path, RectangleF r, Color top, Color bottom)
        {
            using (var b = new LinearGradientBrush(new RectangleF(r.X, r.Y - 0.5f, r.Width, r.Height + 1), top, bottom, 90f)) g.FillPath(b, path);
        }

        // Glass orb (toggle and slider thumbs): soft shadow, white body, thin edge, specular.
        public static void Orb(Graphics g, RectangleF r, bool enabled, bool pressed)
        {
            using (var b = new SolidBrush(Color.FromArgb(Dark ? 120 : 46, 0, 0, 0))) g.FillEllipse(b, r.X - 0.5f, r.Y + 1, r.Width + 1, r.Height + 0.5f);
            Color top = !enabled ? Mix(Surface, TextMuted, 0.3) : pressed ? Hex(0xECEDF0) : Color.White;
            Color bottom = !enabled ? Mix(Surface, TextMuted, 0.5) : pressed ? Hex(0xD9DCE1) : Hex(0xF1F2F5);
            using (var b = new LinearGradientBrush(RectangleF.Inflate(r, 0, 0.5f), top, bottom, 90f)) g.FillEllipse(b, r);
            using (var p = new Pen(HighContrast ? BorderStrong : Color.FromArgb(Dark ? 90 : 40, 0, 0, 0), HighContrast ? 1.2f : 0.8f)) g.DrawEllipse(p, r);
            if (enabled)
                using (var b = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.FillEllipse(b, r.X + r.Width * 0.25f, r.Y + r.Height * 0.1f, r.Width * 0.5f, r.Height * 0.28f);
        }

        public const int RadiusSmall = 6, RadiusMedium = 10, RadiusCard = 16;

        // Duration tokens (ms, Gemini Response 5) for what stays a tween: colour and opacity. Anything that
        // moves uses a spring (Spring.cs). Press is a down-state; Fast tooltips and exits; Standard hover fades.
        public const int PressMs = 50, FastMs = 100, StandardMs = 150, CrossfadeMs = 250,
            FooterHoldMs = 4000, FooterErrorHoldMs = 8000, ReducedFadeMs = 100;

        // Reduced motion: no slides, overshoots, squash or particles; 100 ms opacity fades and instant page
        // switches only. Follows Windows' "Animation effects" unless set in Settings (Settings.ReduceMotion).
        public static bool ReduceMotion;

        public static double EaseOut(double t) { t = Clamp01(t); return 1 - Math.Pow(1 - t, 3); }
        public static double EaseInOut(double t) { t = Clamp01(t); return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; }
        public static double Clamp01(double t) { return t < 0 ? 0 : t > 1 ? 1 : t; }

        // The two physics regimes: chrome decelerates hard, pets overshoot a little.
        public static double Decel(double t) { return Bezier(0.22, 1, 0.36, 1, t); }
        public static double Spring(double t) { return Bezier(0.34, 1.56, 0.64, 1, t); }

        // CSS-style cubic-bezier(x1, y1, x2, y2) at time t: solve x(u) = t (Newton with a bisection
        // fallback), return y(u). y may leave 0..1 for overshooting curves.
        public static double Bezier(double x1, double y1, double x2, double y2, double t)
        {
            t = Clamp01(t);
            if (t == 0 || t == 1 || (x1 == y1 && x2 == y2)) return t;
            double cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx;
            double cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
            double u = t, lo = 0, hi = 1;
            for (int i = 0; i < 8; i++)
            {
                double x = ((ax * u + bx) * u + cx) * u - t;
                if (Math.Abs(x) < 1e-6) break;
                if (x > 0) hi = u; else lo = u;
                double dx = (3 * ax * u + 2 * bx) * u + cx;
                double next = Math.Abs(dx) > 1e-6 ? u - x / dx : -1;
                u = next > lo && next < hi ? next : (lo + hi) / 2;
            }
            return ((ay * u + by) * u + cy) * u;
        }

        // ---- Type: Inter (SIL Open Font License, assets/fonts) ----------------------------------

        // GDI+ draws from a private collection; GDI (TextRenderer, standard controls) finds the same files
        // through AddFontResourceEx. Without the files everything falls back to Segoe UI.
        static PrivateFontCollection fonts;
        static FontFamily inter, interSemibold;

        static void LoadFonts()
        {
            if (fonts != null) return;
            fonts = new PrivateFontCollection();
            string dir = Path.Combine(Path.Combine(Program.Root ?? "", "assets"), "fonts");
            foreach (var name in new[] { "Inter-Regular.ttf", "Inter-SemiBold.ttf", "Inter-Bold.ttf" })
            {
                string path = Path.Combine(dir, name);
                if (!File.Exists(path)) continue;
                try { fonts.AddFontFile(path); AddFontResourceEx(path, 0x10 /* FR_PRIVATE */, IntPtr.Zero); }
                catch (Exception e) { Log.Write("font " + name + ": " + e.Message); }
            }
            inter = fonts.Families.FirstOrDefault(f => f.Name == "Inter");
            interSemibold = fonts.Families.FirstOrDefault(f => f.Name == "Inter SemiBold");
        }

        static Font Make(FontFamily family, float pt, FontStyle style)
        {
            float size = pt * TextScale;
            return family != null ? new Font(family, size, style, GraphicsUnit.Point) : new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }

        public static Font Ui(float pt) { LoadFonts(); return Make(inter, pt, FontStyle.Regular); }

        // Emphasis for labels and selected items.
        public static Font Semibold(float pt)
        {
            LoadFonts();
            return interSemibold != null ? Make(interSemibold, pt, FontStyle.Regular) : Make(inter, pt, FontStyle.Bold);
        }

        // Titles and pet name tags.
        public static Font Heading(float pt) { LoadFonts(); return Make(inter, pt, FontStyle.Bold); }

        public static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        // Linear blend from a to b (t = 0..1), alpha included; drives every colour transition.
        public static Color Mix(Color a, Color b, double t)
        {
            t = Clamp01(t);
            return Color.FromArgb((int)Math.Round(a.A + (b.A - a.A) * t), (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        // Rounded rectangle path (radius in px).
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Specular band: white `topAlpha` at the top edge fading to GlossBottom at half height, clipped to
        // the control's rounded shape.
        public static void Gloss(Graphics g, RectangleF r, float radius, int topAlpha)
        {
            if (topAlpha <= 0 || r.Height < 2 || HighContrast) return;
            var band = new RectangleF(r.X, r.Y, r.Width, (float)Math.Ceiling(r.Height / 2));
            using (var shape = Round(r, radius))
            using (var brush = new LinearGradientBrush(new RectangleF(band.X, band.Y - 0.5f, band.Width, band.Height + 1),
                       Color.FromArgb(Math.Min(255, topAlpha), 255, 255, 255), Color.FromArgb(GlossBottom, 255, 255, 255), 90f))
            {
                var old = g.Clip;
                g.SetClip(shape, CombineMode.Intersect);
                g.FillRectangle(brush, band);
                g.Clip = old;
            }
        }

        // 1 px top inner shadow inside a recessed well (tracks, inputs).
        public static void InnerShadow(Graphics g, RectangleF r, float radius)
        {
            using (var shape = Round(r, radius))
            using (var pen = new Pen(WellInnerShadow, 1f))
            {
                var old = g.Clip;
                g.SetClip(shape, CombineMode.Intersect);
                g.DrawLine(pen, r.X + radius * 0.6f, r.Y + 1f, r.Right - radius * 0.6f, r.Y + 1f);
                g.Clip = old;
            }
        }

        // Duration under reduced motion: movement becomes instant, fades shrink to 100 ms.
        public static int Motion(int ms, bool movement)
        {
            if (!ReduceMotion) return ms;
            return movement ? 0 : Math.Min(ms, ReducedFadeMs);
        }

        // Dark scrollbars for `root` and everything under it (call again for controls added later).
        public static void DarkScrollbars(Control root)
        {
            if (!Dark) return;
            var sc = root as ScrollableControl;
            if ((sc != null && sc.AutoScroll) || root is TextBoxBase)
            {
                var target = root;
                if (target.IsHandleCreated) SetWindowTheme(target.Handle, "DarkMode_Explorer", null);
                else target.HandleCreated += (s, e) => SetWindowTheme(target.Handle, "DarkMode_Explorer", null);
            }
            foreach (Control c in root.Controls) DarkScrollbars(c);
        }

        // "Bottom left" from "BottomLeft": sentence case for labels built from enum names.
        public static string Sentence(string pascal)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in pascal)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ').Append(char.ToLowerInvariant(c));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int AddFontResourceEx(string path, uint flags, IntPtr reserved);
    }

    // A glass card holding a group of rows, iOS Settings style (see-through: the rows show the card, the card
    // shows the backdrop). Hairlines separate its rows. The card's look (backdrop + glass) is rendered once
    // into a bitmap and reused while its size and place on the page stay the same: every row, label and
    // animated control paints the card behind itself, so drawing the glass each time made scrolling lag.
    sealed class GlassCard : TableLayoutPanel
    {
        public const int Side = 20;   // gap between the card and the page edge
        Bitmap look;
        Rectangle lookKey;

        public GlassCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Dock = DockStyle.Fill;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ColumnCount = 1;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Margin = new Padding(Side, 16, Side, 4);
            Padding = new Padding(0, 4, 0, 4);
        }

        // Where the card sits on its page's content (stays put while the page scrolls).
        Point PagePosition()
        {
            Control p = Parent;
            while (p != null && !(p is GlassScroller)) p = p.Parent;
            if (p == null) return Location;
            var at = p.PointToClient(PointToScreen(Point.Empty));
            var scroll = ((ScrollableControl)p).AutoScrollPosition;
            return new Point(at.X - scroll.X, at.Y - scroll.Y);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            var key = new Rectangle(PagePosition(), Size);
            if (look == null || key != lookKey)
            {
                if (look != null) look.Dispose();
                look = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(look))
                {
                    base.OnPaintBackground(new PaintEventArgs(g, ClientRectangle));   // what's behind: the page's backdrop
                    Theme.Glass(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 2), Theme.RadiusCard);
                }
                lookKey = key;
            }
            var r = Rectangle.Intersect(e.ClipRectangle, ClientRectangle);
            if (r.Width > 0 && r.Height > 0) e.Graphics.DrawImage(look, r, r, GraphicsUnit.Pixel);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && look != null) look.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var pen = new Pen(Theme.Border))
                for (int i = 1; i < Controls.Count; i++)
                {
                    var c = Controls[i];
                    if (c.Visible && c.Height > 0 && !(c.Tag is string && (string)c.Tag == "nohair")) e.Graphics.DrawLine(pen, 16, c.Top, Width - 16, c.Top);
                }
        }
    }

    // Menus (Gemini Response 5) fade in on the Bouncy spring and out on Settle instead of vanishing: closing
    // is held back until the fade ends, and a fade that is interrupted carries on from its current opacity.
    // shortcut: items stay clickable during the ~0.3 s fade-out; disable them if double picks ever show up.
    sealed class MenuFade : IAnimated
    {
        readonly ToolStripDropDown menu;
        readonly Spring alpha = new Spring(0, Feel.Bouncy);
        ToolStripDropDownCloseReason reason;
        bool closing, released;

        MenuFade(ToolStripDropDown menu) { this.menu = menu; }

        public static void Attach(ToolStripDropDown menu)
        {
            var fade = new MenuFade(menu);
            menu.Opening += (s, e) =>
            {
                fade.closing = fade.released = false;
                fade.alpha.Tune(Feel.Bouncy);
                fade.alpha.Target = 1;
                try { menu.Opacity = Math.Max(0.01, fade.alpha.Value); } catch (Exception) { }
            };
            menu.Opened += (s, e) => { Theme.RoundCorners(menu.Handle); Animator.Start(fade); };
            menu.Closing += (s, e) =>
            {
                if (fade.released || Theme.ReduceMotion || !menu.Visible) return;   // the fade-out is over: let it close
                e.Cancel = true;
                fade.reason = e.CloseReason;
                fade.closing = true;
                fade.alpha.Tune(Feel.Settle);
                fade.alpha.Target = 0;
                Animator.Start(fade);
            };
        }

        public bool Step(double dt)
        {
            try
            {
                if (menu.IsDisposed || !menu.Visible) return false;
                bool moving = alpha.Step(dt);
                menu.Opacity = Math.Max(0.01, Math.Min(1, alpha.Value));
                if (closing && (!moving || alpha.Value < 0.02)) { released = true; alpha.Snap(0); menu.Close(reason); return false; }
                return moving;
            }
            catch (Exception) { return false; }
        }
    }

    // Tag of a tray menu agent row: the name is drawn apart from its grey " — state (via)" suffix.
    sealed class MenuAgent
    {
        public string Name;
        public bool Active, Muted;   // Active: has a live pet (semibold name); Muted: not installed
    }

    // Flat themed menu (styled popover instead of the default Win32 menu). In menus without a check
    // margin every label starts at x 34 and status dots sit centred on x 18 (Gemini Response 3).
    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new Colors()) { RoundedEdges = false; }

        const int TextX = 34, DotX = 18;

        static bool Aligned(ToolStripItem item)
        {
            var menu = item.Owner as ToolStripDropDownMenu;
            return menu != null && !menu.ShowCheckMargin;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var item = e.Item;
            var rect = e.TextRectangle;
            if (Aligned(item) && !(item is ToolStripSeparator))
                rect = new Rectangle(TextX, rect.Y, Math.Max(0, item.Width - TextX - 24), rect.Height);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            var agent = item.Tag as MenuAgent;
            if (agent == null || e.Text == null || !e.Text.StartsWith(agent.Name, StringComparison.Ordinal))
            {
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, rect, item.Enabled ? Theme.TextPrimary : Theme.TextMuted, flags);
                return;
            }
            string suffix = e.Text.Substring(agent.Name.Length);
            using (var nameFont = agent.Active ? Theme.Semibold(9F) : Theme.Ui(9F))
            {
                int w = TextRenderer.MeasureText(e.Graphics, agent.Name, nameFont, rect.Size, flags).Width;
                TextRenderer.DrawText(e.Graphics, agent.Name, nameFont, rect, agent.Muted ? Theme.TextMuted : Theme.TextPrimary, flags);
                var rest = new Rectangle(rect.X + w, rect.Y, Math.Max(0, rect.Width - w), rect.Height);
                TextRenderer.DrawText(e.Graphics, suffix, e.TextFont, rest, agent.Muted ? Theme.TextMuted : Theme.TextSecondary, flags);
            }
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image == null || !Aligned(e.Item)) { base.OnRenderItemImage(e); return; }
            var r = e.ImageRectangle;
            e.Graphics.DrawImage(e.Image, DotX - e.Image.Width / 2, r.Y + (r.Height - e.Image.Height) / 2, e.Image.Width, e.Image.Height);
        }

        // 16×16 status dot images for agent rows (solid; hollow for "not installed").
        static readonly System.Collections.Generic.Dictionary<DotKind, Image> dots = new System.Collections.Generic.Dictionary<DotKind, Image>();
        static bool dotsDark;

        public static Image Dot(DotKind kind)
        {
            Image img;
            if (dotsDark != Theme.Dark) { dots.Clear(); dotsDark = Theme.Dark; }   // Appearance switched: colours changed
            if (dots.TryGetValue(kind, out img)) return img;
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var c = StatusDot.ColorOf(kind);
                if (kind == DotKind.NotInstalled) using (var p = new Pen(c, 1.5f)) g.DrawEllipse(p, 4.5f, 4.5f, 7, 7);
                else using (var b = new SolidBrush(c)) g.FillEllipse(b, 4, 4, 8, 8);
            }
            dots[kind] = bmp;
            return bmp;
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(Theme.BorderStrong)) e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.Hover)) using (var path = Theme.Round(r, 6)) e.Graphics.FillPath(b, path);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextSecondary;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            float cx = Aligned(e.Item) ? DotX : r.X + r.Width / 2f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.Accent))
                e.Graphics.FillEllipse(b, cx - 3, r.Y + r.Height / 2f - 3, 6, 6);   // 6 px dot, not a tick box
        }

        sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Elevated; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Elevated; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Elevated; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Elevated; } }
            public override Color MenuBorder { get { return Theme.BorderStrong; } }
            public override Color MenuItemBorder { get { return Theme.Hover; } }
            public override Color MenuItemSelected { get { return Theme.Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Elevated; } }
            public override Color CheckBackground { get { return Theme.Elevated; } }
            public override Color CheckSelectedBackground { get { return Theme.Hover; } }
            public override Color CheckPressedBackground { get { return Theme.Hover; } }
        }
    }
}
