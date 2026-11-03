using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace OpaGachi
{
    // Line icons, 1.5 px round strokes, absolute SVG path commands only (M L H V C Q A Z), parsed once into
    // GraphicsPaths on a 20×20 grid and scaled when drawn. Gemini Response 3 (4.5) drew on 20; Response 4
    // redrew nav (24), status (16) and action (14) icons, which are scaled to 20 when parsed (Grid).
    static class Icons
    {
        static readonly Dictionary<string, string> Source = new Dictionary<string, string>
        {
            { "nav-pets", "M 4.5 9.5 L 3.5 4 L 9 5.5 C 10 5 11 4.75 12 4.75 C 13 4.75 14 5 15 5.5 L 20.5 4 L 19.5 9.5 C 21 11.5 21.5 14 21 16 C 20 20 16.5 21.5 12 21.5 C 7.5 21.5 4 20 3 16 C 2.5 14 3 11.5 4.5 9.5 Z M 9 13 L 9 13.8 M 15 13 L 15 13.8" },
            { "nav-agents", "M 9.5 3 L 14.5 3 L 13.5 7 L 10.5 7 Z M 10.5 7 L 8 13.5 L 12 21 L 16 13.5 L 13.5 7" },
            { "nav-connections", "M 8.5 2.5 L 8.5 6.5 M 15.5 2.5 L 15.5 6.5 M 6 6.5 L 18 6.5 C 18.5 10 18.5 13 15 15 L 15 17.5 L 9 17.5 L 9 15 C 5.5 13 5.5 10 6 6.5 Z M 12 17.5 L 12 21.5" },
            { "nav-usage", "M 4.2 17.5 A 9.5 9.5 0 1 1 19.8 17.5 M 12 13.5 L 16.5 9 M 13.5 13.5 A 1.5 1.5 0 1 0 10.5 13.5 A 1.5 1.5 0 1 0 13.5 13.5" },
            { "method-hook", "M 10.0 3.0 V 10.5 C 10.0 12.4 8.4 14.0 6.5 14.0 C 4.6 14.0 3.0 12.4 3.0 10.5 C 3.0 9.0 4.0 7.8 5.4 7.3 M 10.0 5.5 L 13.5 2.5 M 13.5 2.5 H 10.5 M 13.5 2.5 V 5.5" },
            { "method-app", "M 3.0 4.0 H 17.0 V 16.0 H 3.0 V 4.0 Z M 3.0 7.5 H 17.0 M 5.5 5.8 H 5.6 M 8.0 5.8 H 8.1" },
            { "method-logs", "M 5.0 3.0 H 13.0 L 16.0 6.0 V 17.0 H 5.0 V 3.0 Z M 13.0 3.0 V 6.0 H 16.0 M 7.5 9.5 H 13.5 M 7.5 12.5 H 13.5 M 7.5 15.0 H 11.0" },
            { "state-working", "M 8 2 A 6 6 0 1 1 2.3 10" },
            { "state-needs-you", "M 8 1.75 A 6.25 6.25 0 1 0 8 14.25 A 6.25 6.25 0 1 0 8 1.75 Z M 8 4.5 L 8 8.5 M 8 11.4 L 8 11.6" },
            { "state-done", "M 3 8.5 L 6.5 12 L 13 4" },
            { "state-failed", "M 4 4 L 12 12 M 12 4 L 4 12" },
            { "state-off", "M 10.0 4.0 A 6.0 6.0 0 1 0 10.0 16.0 A 6.0 6.0 0 1 0 10.0 4.0 Z M 7.0 10.0 H 13.0" },
            { "state-not-installed", "M 10.0 4.0 A 6.0 6.0 0 1 0 10.0 16.0 A 6.0 6.0 0 1 0 10.0 4.0 Z M 5.8 14.2 L 14.2 5.8" },
            { "action-show-window", "M 6.5 2.5 L 2.5 2.5 L 2.5 11.5 L 11.5 11.5 L 11.5 7.5 M 8 2 L 12 2 L 12 6 M 5.5 8.5 L 12 2" },
            { "action-settings", "M 7 4.75 A 2.25 2.25 0 1 0 7 9.25 A 2.25 2.25 0 1 0 7 4.75 Z M 7 1 L 7 2.5 M 7 11.5 L 7 13 M 1 7 L 2.5 7 M 11.5 7 L 13 7 M 2.75 2.75 L 3.8 3.8 M 10.2 10.2 L 11.25 11.25 M 2.75 11.25 L 3.8 10.2 M 10.2 3.8 L 11.25 2.75" },
            { "action-demo", "M 6.0 4.5 L 15.5 10.0 L 6.0 15.5 V 4.5 Z" },
            { "action-log", "M 3 1.5 L 8.5 1.5 L 11.5 4.5 L 11.5 12.5 L 3 12.5 Z M 8.5 1.5 L 8.5 4.5 L 11.5 4.5 M 5 7.5 L 9 7.5 M 5 9.5 L 8 9.5" },
            { "action-copy", "M 7.5 6.5 H 15.5 V 16.5 H 7.5 V 6.5 Z M 4.5 13.5 V 3.5 H 12.5" },
            { "nav-help", "M 12 2.5 A 9.5 9.5 0 1 0 12 21.5 A 9.5 9.5 0 1 0 12 2.5 Z M 9.5 9 C 9.5 7.5 10.5 6.5 12 6.5 C 13.5 6.5 14.5 7.5 14.5 8.75 C 14.5 10.25 12 11.25 12 13 M 12 16.4 L 12 16.6" },
            { "state-idle", "M 12.5 10 A 5.5 5.5 0 1 1 6 3.5 C 6 4.5 6.5 5.5 7.2 6.3 C 8.5 7.6 10.5 8 12.5 10 Z" },
            { "state-stale", "M 8 2 A 6 6 0 1 0 8 14 A 6 6 0 1 0 8 2 Z M 8 4.5 L 8 8 L 10.5 9.5" },
            { "action-refresh", "M 12 7 A 5 5 0 1 1 10.5 3.5 L 12.5 3.5 M 12.5 1.5 L 12.5 4 L 10 4" },
            { "action-close", "M 3 3 L 11 11 M 11 3 L 3 11" },
            { "action-check", "M 2.5 7.5 L 5.5 10.5 L 11.5 3.5" },
            { "glyph-tie", "M 7.5 2.5 H 12.5 L 11.5 5.5 H 8.5 L 7.5 2.5 Z M 8.5 5.5 H 11.5 L 13.0 14.0 L 10.0 17.5 L 7.0 14.0 L 8.5 5.5 Z" },
            // Category tiles on the Settings lists (2026-11-02), drawn white on a tinted square.
            { "cat-size", "M 4.5 15.5 L 15.5 4.5 M 10.5 4.5 H 15.5 V 9.5 M 4.5 10.5 V 15.5 H 9.5" },
            { "cat-pin", "M 10 18 C 10 18 4.5 12.2 4.5 8.2 A 5.5 5.5 0 0 1 15.5 8.2 C 15.5 12.2 10 18 10 18 Z M 10 6.3 A 1.9 1.9 0 1 0 10 10.1 A 1.9 1.9 0 1 0 10 6.3 Z" },
            { "cat-sparkle", "M 9 3 L 10.5 7.5 L 15 9 L 10.5 10.5 L 9 15 L 7.5 10.5 L 3 9 L 7.5 7.5 Z M 15 13 L 15.7 15 L 17.5 15.7 L 15.7 16.4 L 15 18.3 L 14.3 16.4 L 12.5 15.7 L 14.3 15 Z" },
            { "cat-cursor", "M 5.5 3 V 16 L 9 12.8 L 11.5 18 L 13.7 17 L 11.3 11.8 L 16 11.5 Z" },
            { "cat-theme", "M 10 3 A 7 7 0 1 0 10 17 A 7 7 0 1 0 10 3 Z M 10 3 V 17 M 10 6 H 15.5 M 10 9.3 H 16.9 M 10 12.7 H 16.9 M 10 14 H 15.5" },
            { "cat-access", "M 10 2.8 A 1.7 1.7 0 1 0 10 6.2 A 1.7 1.7 0 1 0 10 2.8 Z M 4 8 L 10 9 L 16 8 M 10 9 V 12.5 M 10 12.5 L 7.5 17.5 M 10 12.5 L 12.5 17.5" },
            { "cat-sound", "M 3.5 8 H 6.5 L 10.5 4.5 V 15.5 L 6.5 12 H 3.5 Z M 13 7.5 A 3.5 3.5 0 0 1 13 12.5 M 15.2 5.2 A 6.8 6.8 0 0 1 15.2 14.8" },
            { "cat-flask", "M 8 3 H 12 M 8.8 3 V 8.2 L 4.5 15.5 C 4 16.5 4.6 17.5 5.7 17.5 H 14.3 C 15.4 17.5 16 16.5 15.5 15.5 L 11.2 8.2 V 3 M 6.6 12.5 H 13.4" },
            { "cat-lock", "M 5 9 H 15 V 17 H 5 Z M 7 9 V 6.5 A 3 3 0 0 1 13 6.5 V 9 M 10 12 V 14" },
            { "cat-power", "M 10 3 V 9.5 M 6.2 5.6 A 6 6 0 1 0 13.8 5.6" },
            { "cat-star", "M 10 2.8 L 12.2 7.4 L 17.2 8 L 13.5 11.4 L 14.5 16.4 L 10 13.9 L 5.5 16.4 L 6.5 11.4 L 2.8 8 L 7.8 7.4 Z" },
        };

        // Icons drawn on another grid than 20 (Gemini Response 4).
        static readonly Dictionary<string, float> Grid = new Dictionary<string, float>
        {
            { "nav-pets", 24 }, { "nav-agents", 24 }, { "nav-connections", 24 }, { "nav-usage", 24 }, { "nav-help", 24 },
            { "state-working", 16 }, { "state-needs-you", 16 }, { "state-done", 16 }, { "state-failed", 16 }, { "state-idle", 16 }, { "state-stale", 16 },
            { "action-settings", 14 }, { "action-log", 14 }, { "action-show-window", 14 }, { "action-refresh", 14 }, { "action-close", 14 }, { "action-check", 14 },
        };

        static readonly Dictionary<string, GraphicsPath> parsed = new Dictionary<string, GraphicsPath>();

        // The icon's path on its own grid (shared: clone before transforming).
        public static GraphicsPath Get(string name)
        {
            GraphicsPath p;
            if (parsed.TryGetValue(name, out p)) return p;
            string d;
            p = Source.TryGetValue(name, out d) ? Parse(d) : new GraphicsPath();
            float grid;
            if (Grid.TryGetValue(name, out grid)) using (var m = new Matrix(20f / grid, 0, 0, 20f / grid, 0, 0)) p.Transform(m);
            parsed[name] = p;
            return p;
        }

        // Stroke the icon into a size×size box at (x, y). The stroke stays `stroke` device pixels wide.
        public static void Draw(Graphics g, string name, float x, float y, float size, Color color, float stroke)
        {
            Stroke(g, Get(name), 20f, x, y, size, color, stroke);
        }

        // Filled icon (e.g. the tie glyph in its tie colour) with a thin outline.
        public static void Fill(Graphics g, string name, float x, float y, float size, Color fill, Color outline, float stroke)
        {
            using (var p = (GraphicsPath)Get(name).Clone())
            using (var m = new Matrix(size / 20f, 0, 0, size / 20f, x, y))
            {
                p.Transform(m);
                var oldMode = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                if (stroke > 0) using (var pen = RoundPen(outline, stroke)) g.DrawPath(pen, p);
                g.SmoothingMode = oldMode;
            }
        }

        // Any path drawn from its `grid`-unit square into a size×size box.
        public static void Stroke(Graphics g, GraphicsPath source, float grid, float x, float y, float size, Color color, float stroke)
        {
            using (var p = (GraphicsPath)source.Clone())
            using (var m = new Matrix(size / grid, 0, 0, size / grid, x, y))
            using (var pen = RoundPen(color, stroke))
            {
                p.Transform(m);
                var oldMode = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawPath(pen, p);
                g.SmoothingMode = oldMode;
            }
        }

        public static Pen RoundPen(Color color, float width)
        {
            return new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        }

        // ---- Absolute SVG path data → GraphicsPath --------------------------------------------------------

        public static GraphicsPath Parse(string d)
        {
            var path = new GraphicsPath();
            var tokens = Tokenize(d);
            int i = 0;
            char cmd = 'M';
            PointF cur = PointF.Empty, start = PointF.Empty;
            bool open = false;
            while (i < tokens.Count)
            {
                if (char.IsLetter(tokens[i][0])) cmd = tokens[i++][0];
                switch (cmd)
                {
                    case 'M':
                        cur = start = new PointF(Num(tokens, ref i), Num(tokens, ref i));
                        path.StartFigure();
                        open = false;
                        cmd = 'L';   // further pairs are implicit line-tos
                        break;
                    case 'L':
                    {
                        var p = new PointF(Num(tokens, ref i), Num(tokens, ref i));
                        path.AddLine(cur, p);
                        cur = p; open = true;
                        break;
                    }
                    case 'H':
                    {
                        var p = new PointF(Num(tokens, ref i), cur.Y);
                        path.AddLine(cur, p);
                        cur = p; open = true;
                        break;
                    }
                    case 'V':
                    {
                        var p = new PointF(cur.X, Num(tokens, ref i));
                        path.AddLine(cur, p);
                        cur = p; open = true;
                        break;
                    }
                    case 'C':
                    {
                        var c1 = new PointF(Num(tokens, ref i), Num(tokens, ref i));
                        var c2 = new PointF(Num(tokens, ref i), Num(tokens, ref i));
                        var p = new PointF(Num(tokens, ref i), Num(tokens, ref i));
                        path.AddBezier(cur, c1, c2, p);
                        cur = p; open = true;
                        break;
                    }
                    case 'Q':
                    {
                        var q = new PointF(Num(tokens, ref i), Num(tokens, ref i));
                        var p = new PointF(Num(tokens, ref i), Num(tokens, ref i));
                        var c1 = new PointF(cur.X + 2f / 3f * (q.X - cur.X), cur.Y + 2f / 3f * (q.Y - cur.Y));
                        var c2 = new PointF(p.X + 2f / 3f * (q.X - p.X), p.Y + 2f / 3f * (q.Y - p.Y));
                        path.AddBezier(cur, c1, c2, p);
                        cur = p; open = true;
                        break;
                    }
                    case 'A':
                    {
                        float rx = Num(tokens, ref i), ry = Num(tokens, ref i), rot = Num(tokens, ref i);
                        bool large = Num(tokens, ref i) != 0, sweep = Num(tokens, ref i) != 0;
                        var p = new PointF(Num(tokens, ref i), Num(tokens, ref i));
                        AddArc(path, cur, rx, ry, rot, large, sweep, p);
                        cur = p; open = true;
                        break;
                    }
                    case 'Z':
                        if (open) path.CloseFigure();
                        cur = start; open = false;
                        if (i < tokens.Count && !char.IsLetter(tokens[i][0])) i++;   // stray number after Z
                        break;
                    default:
                        i++;   // unknown command: skip a token rather than loop forever
                        break;
                }
            }
            return path;
        }

        static List<string> Tokenize(string d)
        {
            var list = new List<string>();
            var sb = new System.Text.StringBuilder();
            Action flush = () => { if (sb.Length > 0) { list.Add(sb.ToString()); sb.Length = 0; } };
            foreach (char c in d)
            {
                if (char.IsLetter(c) && c != 'e' && c != 'E') { flush(); list.Add(c.ToString()); }
                else if (c == ' ' || c == ',' || c == '\t' || c == '\n' || c == '\r') flush();
                else if (c == '-' && sb.Length > 0 && sb[sb.Length - 1] != 'e' && sb[sb.Length - 1] != 'E') { flush(); sb.Append(c); }
                else sb.Append(c);
            }
            flush();
            return list;
        }

        static float Num(List<string> tokens, ref int i)
        {
            if (i >= tokens.Count) return 0;
            float v;
            float.TryParse(tokens[i++], NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }

        // SVG endpoint arc → centre form → cubic Béziers of at most 90° each.
        static void AddArc(GraphicsPath path, PointF p0, float rxIn, float ryIn, float rotDeg, bool large, bool sweep, PointF p1)
        {
            if (p0 == p1) return;
            double rx = Math.Abs(rxIn), ry = Math.Abs(ryIn);
            if (rx < 1e-6 || ry < 1e-6) { path.AddLine(p0, p1); return; }
            double phi = rotDeg * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (p0.X - p1.X) / 2.0, dy = (p0.Y - p1.Y) / 2.0;
            double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
            double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
            if (lambda > 1) { double s = Math.Sqrt(lambda); rx *= s; ry *= s; }
            double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            double coef = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
            double cxp = coef * rx * y1 / ry, cyp = -coef * ry * x1 / rx;
            double cx = cos * cxp - sin * cyp + (p0.X + p1.X) / 2.0, cy = sin * cxp + cos * cyp + (p0.Y + p1.Y) / 2.0;
            double ux = (x1 - cxp) / rx, uy = (y1 - cyp) / ry, vx = (-x1 - cxp) / rx, vy = (-y1 - cyp) / ry;
            double theta = Math.Atan2(uy, ux);
            double delta = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            if (!sweep && delta > 0) delta -= 2 * Math.PI;
            if (sweep && delta < 0) delta += 2 * Math.PI;
            int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9));
            double step = delta / segments, k = 4.0 / 3.0 * Math.Tan(step / 4);
            for (int s = 0; s < segments; s++)
            {
                double a = theta + s * step, b = a + step;
                PointF e0 = Ellipse(cx, cy, rx, ry, cos, sin, a), e1 = Ellipse(cx, cy, rx, ry, cos, sin, b);
                PointF d0 = Tangent(rx, ry, cos, sin, a), d1 = Tangent(rx, ry, cos, sin, b);
                if (s == 0) e0 = p0;
                if (s == segments - 1) e1 = p1;
                path.AddBezier(e0, new PointF((float)(e0.X + k * d0.X), (float)(e0.Y + k * d0.Y)),
                    new PointF((float)(e1.X - k * d1.X), (float)(e1.Y - k * d1.Y)), e1);
            }
        }

        static PointF Ellipse(double cx, double cy, double rx, double ry, double cos, double sin, double t)
        {
            double x = rx * Math.Cos(t), y = ry * Math.Sin(t);
            return new PointF((float)(cx + x * cos - y * sin), (float)(cy + x * sin + y * cos));
        }

        static PointF Tangent(double rx, double ry, double cos, double sin, double t)
        {
            double x = -rx * Math.Sin(t), y = ry * Math.Cos(t);
            return new PointF((float)(x * cos - y * sin), (float)(x * sin + y * cos));
        }
    }
}
