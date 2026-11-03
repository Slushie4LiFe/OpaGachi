using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace OpaGachi
{
    // Status badges by a pet's head (Gemini Response 3, 4.6), drawn as vectors on a 24×24 grid so they stay
    // crisp at every pet size: a dark cut-out rim, a gradient squircle body with a light inner rim, a curved
    // top gloss, then the glyph. ! needs you (amber), ✓ complete (green), ✗ failed (coral), … thinking
    // (graphite, dots wave with the sprite frame), zz bored (slate, the z's bob with the sprite frame).
    static class BadgeArt
    {
        sealed class Style
        {
            public Color Top, Bottom, Rim, Glyph;
            public int RimAlpha;
            public float Stroke;
            public GraphicsPath Path, Path2;
        }

        static readonly Dictionary<string, Style> Styles = new Dictionary<string, Style>
        {
            { "help", new Style { Top = Theme.Hex(0xF2B64B), Bottom = Theme.Hex(0xD98824), Rim = Theme.Hex(0xFFE099), RimAlpha = 180, Glyph = Theme.Hex(0x111317), Stroke = 2.6f,
                Path = Icons.Parse("M 12.0 6.5 V 13.2 M 12.0 16.8 V 17.2") } },
            { "complete", new Style { Top = Theme.Hex(0x46D98B), Bottom = Theme.Hex(0x26A862), Rim = Theme.Hex(0xA3F5C8), RimAlpha = 180, Glyph = Theme.Hex(0x111317), Stroke = 2.5f } },
            { "fail", new Style { Top = Theme.Hex(0xF06565), Bottom = Theme.Hex(0xC93B3B), Rim = Theme.Hex(0xFFB3B3), RimAlpha = 170, Glyph = Theme.Hex(0xECEFF4), Stroke = 2.4f,
                Path = Icons.Parse("M 8.2 8.2 L 15.8 15.8 M 15.8 8.2 L 8.2 15.8") } },
            { "think", new Style { Top = Theme.Hex(0x2E333D), Bottom = Theme.Hex(0x1C1F26), Rim = Theme.Accent, RimAlpha = 220, Glyph = Theme.TextPrimary, Stroke = 0 } },
            { "bored", new Style { Top = Theme.Hex(0x262A33), Bottom = Theme.Hex(0x16181D), Rim = Theme.TextMuted, RimAlpha = 180, Glyph = Theme.TextSecondary, Stroke = 1.8f,
                Path = Icons.Parse("M 6.5 12.5 H 10.5 L 6.5 16.5 H 10.5"), Path2 = Icons.Parse("M 11.8 7.2 H 17.2 L 11.8 12.5 H 17.2") } },
        };

        static readonly GraphicsPath Gloss = Icons.Parse("M 3.5 7.5 C 3.5 4.8 5.3 3.2 8.0 3.2 H 16.0 C 18.7 3.2 20.5 4.8 20.5 7.5 V 9.2 Q 12.0 12.2 3.5 9.2 Z");
        static readonly RectangleF Outer = new RectangleF(1, 1, 22, 22), Body = new RectangleF(2.5f, 2.5f, 19, 19);

        public static bool Known(string name) { return name != null && Styles.ContainsKey(name); }

        // Draws in 24-grid units (the caller sets the transform). `old`/`mix`: during a swap the body colour
        // cross-fades from the old badge's; `oldSymbol` shows the outgoing glyph while it shrinks away.
        public static void Draw(Graphics g, Pose p)
        {
            Style s, o;
            if (!Styles.TryGetValue(p.Badge, out s)) return;
            if (p.BadgeOld == null || !Styles.TryGetValue(p.BadgeOld, out o)) o = s;
            float alpha = Math.Max(0, Math.Min(1, p.BadgeAlpha)), mix = p.BadgeMix;
            Func<int, int> A = a => (int)(a * alpha);

            using (var outer = Theme.Round(Outer, 6.5f))
            using (var b = new SolidBrush(Color.FromArgb(A(245), Theme.Well))) g.FillPath(b, outer);
            using (var body = Theme.Round(Body, 5.2f))
            {
                Color top = Theme.Mix(o.Top, s.Top, mix), bottom = Theme.Mix(o.Bottom, s.Bottom, mix), rim = Theme.Mix(o.Rim, s.Rim, mix);
                using (var b = new LinearGradientBrush(new RectangleF(Body.X, Body.Y - 0.5f, Body.Width, Body.Height + 1), Color.FromArgb(A(255), top), Color.FromArgb(A(255), bottom), 90f))
                    g.FillPath(b, body);
                using (var inner = Theme.Round(RectangleF.Inflate(Body, -0.5f, -0.5f), 4.7f))
                using (var pen = new Pen(Color.FromArgb(A((int)(o.RimAlpha + (s.RimAlpha - o.RimAlpha) * mix)), rim), 1f)) g.DrawPath(pen, inner);
                using (var b = new LinearGradientBrush(new RectangleF(3.5f, 2.5f, 17, 9.5f), Color.FromArgb(A(95), 255, 255, 255), Color.FromArgb(A(18), 255, 255, 255), 90f))
                    g.FillPath(b, Gloss);
                if (p.Glint >= 0 && p.Glint <= 1)   // a diagonal glint sweeping left to right
                {
                    var old = g.Clip;
                    g.SetClip(body, CombineMode.Intersect);
                    float x = 1 + 22 * p.Glint;
                    using (var band = new GraphicsPath())
                    {
                        band.AddPolygon(new[] { new PointF(x - 2, 2), new PointF(x + 2, 2), new PointF(x - 3, 22), new PointF(x - 7, 22) });
                        using (var b = new SolidBrush(Color.FromArgb(A(110), 255, 255, 255))) g.FillPath(b, band);
                    }
                    g.Clip = old;
                }
            }

            var glyph = p.SymbolOld ? o : s;
            string glyphName = p.SymbolOld ? p.BadgeOld : p.Badge;
            var state = g.Save();
            if (p.SymbolScale != 1)
            {
                g.TranslateTransform(12, 12);
                g.ScaleTransform(p.SymbolScale, p.SymbolScale);
                g.TranslateTransform(-12, -12);
            }
            Color ink = Color.FromArgb(A(255), glyph.Glyph);
            switch (glyphName)
            {
                case "think":
                    for (int i = 0; i < 3; i++)
                    {
                        // Dots wave up in turn over each 12-frame (1 s) cycle, tinting grey → teal → white.
                        double q = ((p.Frame - 2 * i) % 12 + 12) % 12 / 12.0;
                        double bump = q < 0.5 ? Math.Sin(Math.PI * q * 2) : 0;
                        Color c = bump < 0.5 ? Theme.Mix(Theme.TextSecondary, Theme.Accent, bump * 2) : Theme.Mix(Theme.Accent, Theme.TextPrimary, (bump - 0.5) * 2);
                        float cx = 7.5f + 4.5f * i, cy = (float)(12 - 2.2 * bump);
                        using (var b = new SolidBrush(Color.FromArgb(A(255), c))) g.FillEllipse(b, cx - 1.6f, cy - 1.6f, 3.2f, 3.2f);
                    }
                    break;
                case "complete":
                    using (var pen = Icons.RoundPen(ink, glyph.Stroke)) DrawCheck(g, pen, p.SymbolOld ? 1 : p.CheckDraw);
                    break;
                case "bored":
                {
                    float bob = (p.Frame / 6) % 2 == 0 ? -1 : 1;   // the two z's take turns every 6 frames
                    using (var pen = Icons.RoundPen(ink, glyph.Stroke))
                    {
                        var st = g.Save();
                        g.TranslateTransform(0, -bob * 0.5f);
                        g.DrawPath(pen, glyph.Path);
                        g.Restore(st);
                        st = g.Save();
                        g.TranslateTransform(0, bob * 0.5f);
                        g.DrawPath(pen, glyph.Path2);
                        g.Restore(st);
                    }
                    break;
                }
                default:
                    if (glyph.Path != null) using (var pen = Icons.RoundPen(ink, glyph.Stroke)) g.DrawPath(pen, glyph.Path);
                    break;
            }
            g.Restore(state);
        }

        // ✓ drawn from its short stroke into its long one, `k` = 0..1 of the way.
        static void DrawCheck(Graphics g, Pen pen, float k)
        {
            var a = new PointF(7.2f, 12.4f);
            var b = new PointF(10.5f, 15.8f);
            var c = new PointF(17.0f, 8.5f);
            const float first = 4.74f, second = 9.77f;
            float drawn = (first + second) * Math.Max(0, Math.Min(1, k));
            if (drawn <= 0.05f) return;
            float f1 = Math.Min(1, drawn / first);
            g.DrawLine(pen, a, new PointF(a.X + (b.X - a.X) * f1, a.Y + (b.Y - a.Y) * f1));
            if (drawn > first)
            {
                float f2 = Math.Min(1, (drawn - first) / second);
                g.DrawLine(pen, b, new PointF(b.X + (c.X - b.X) * f2, b.Y + (c.Y - b.Y) * f2));
            }
        }
    }
}
