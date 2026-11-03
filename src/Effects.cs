using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace OpaGachi
{
    enum FxKind { Complete, Small, Idle, Mote, Shimmer }

    // Per-character effects (Gemini Response 3, 4.2): a few GDI+ shapes per pet that live 0.25–0.9 s, on
    // Complete and Thank (full burst), Arrival and Hover (small), the occasional idle check (subtle), plus
    // ChatGPT's image-making motes and the collar shimmer that hides a pose
    // change. Positions are in Normal-size pixels inside the figure box (160 px tall) and scale with the
    // pet; drag is per second (Gemini gave it per frame), so effects look the same at any frame rate.
    // The pet window only ticks at 30 fps while something here is alive.
    sealed class Effects
    {
        enum Shape { Star4, Twinkle, Diamond, Orb, Prism, TwinDot, Slash, Spark, Orbit, Accretion }
        enum Curve { HoldThenSquare, Bell, PopHoldCubic, FlashSnap, Linear }

        sealed class Particle
        {
            public Shape Shape;
            public Curve Curve;
            public float X, Y, Vx, Vy, Ay, Drag = 1, Size, Rot, Spin, Life, Age, Phase, Sway, Wave;
            public Color C1, C2;
        }

        sealed class Ring
        {
            public double Start, Life;
            public float W0, W1, A0, Stroke, Spin, Sweep;
            public Color Color;
        }

        sealed class Pending { public double At; public FxKind Kind; }

        readonly string agent;
        public string Style;
        public string Trigger = "Both";
        public void Interaction(bool click, double now)
        {
            if (Trigger == "Both" || Trigger == (click ? "On Click" : "On Hover")) Burst(FxKind.Small, now);
        }
        readonly Random random;
        readonly List<Particle> particles = new List<Particle>();
        readonly List<Ring> rings = new List<Ring>();
        readonly List<Pending> pending = new List<Pending>();
        double last = -1;
        const float BoxW = 147, BoxH = 160;   // the Normal figure box

        public Effects(string agent, Random random)
        {
            this.agent = agent == "demo" ? "claude" : agent;
            this.random = random;
        }

        public bool Alive { get { return particles.Count > 0 || rings.Count > 0 || pending.Count > 0; } }

        public void Burst(FxKind kind, double now) { Burst(kind, now, 0); }

        public void Burst(FxKind kind, double now, double delay)
        {
            if (Theme.ReduceMotion) return;
            if (delay > 0) { pending.Add(new Pending { At = now + delay, Kind = kind }); return; }
            Spawn(kind, now);
        }

        float R(float a, float b) { return a + (float)random.NextDouble() * (b - a); }
        float X(float a, float b) { return R(a, b) / 100 * BoxW; }
        float Y(float a, float b) { return R(a, b) / 100 * BoxH; }

        void Add(Shape shape, Curve curve, float x, float y, float vx, float vy, float life, float size, Color c1, Color c2)
        {
            particles.Add(new Particle { Shape = shape, Curve = curve, X = x, Y = y, Vx = vx, Vy = vy, Life = life, Size = size, C1 = c1, C2 = c2, Phase = R(0, 6.28f) });
        }

        Particle Last { get { return particles[particles.Count - 1]; } }

        void GroundRing(double now, float w0, float w1, float life, Color color, float alpha, float stroke)
        {
            rings.Add(new Ring { Start = now, Life = life, W0 = w0, W1 = w1, A0 = alpha, Stroke = stroke, Color = color });
        }

        static Color A(int alpha, int rgb) { return Color.FromArgb(alpha, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        void Spawn(FxKind kind, double now)
        {
            if (kind == FxKind.Shimmer)
            {
                // Six small sparkles at the collar, 250 ms: they hide the jump between sitting and standing.
                for (int i = 0; i < 6; i++) Add(Shape.Spark, Curve.Linear, X(38, 62), Y(38, 50), R(-6, 6), R(-14, -4), 0.25f, R(1.5f, 2.5f), A(90, 0xECEFF4), Color.Empty);
                return;
            }
            int first = particles.Count, firstRing = rings.Count;
            Gemini(kind, now);
            var colors = SparklePalette.Stops(Style ?? NameFill.DefaultFor(agent));
            for (int i = first; i < particles.Count; i++)
            {
                particles[i].C1 = Color.FromArgb(245, colors[(i-first) % colors.Length]);
                particles[i].C2 = Color.FromArgb(230, colors[(i-first+1) % colors.Length]);
            }
            for (int i = firstRing; i < rings.Count; i++) rings[i].Color = colors[(i-firstRing) % colors.Length];
        }

        // Terracotta spark-burst and rising craft motes, over a teal ground ring.
        void Claude(FxKind kind, double now)
        {
            int sparks = kind == FxKind.Complete ? 8 : kind == FxKind.Idle ? 3 : 0, motes = kind == FxKind.Complete ? 6 : kind == FxKind.Small ? 4 : 0;
            for (int i = 0; i < sparks; i++)
            {
                double angle = R(-150, -30) * Math.PI / 180, speed = kind == FxKind.Idle ? R(20, 32) : R(48, 75);
                Add(Shape.Star4, Curve.HoldThenSquare, X(22, 78), Y(12, 42), (float)(Math.Cos(angle) * speed), (float)(Math.Sin(angle) * speed), 0.75f,
                    kind == FxKind.Idle ? R(4, 6) : R(6, 10), i % 3 == 0 ? A(220, 0xFFD1A6) : A(240, 0xD97757), Color.Empty);
                Last.Drag = 0.88f; Last.Spin = 60;
            }
            for (int i = 0; i < motes; i++)
            {
                Add(Shape.Diamond, Curve.HoldThenSquare, X(38, 62), Y(38, 52), 0, -28, 0.75f, 4, A(220, 0xFFD1A6), Color.Empty);
                Last.Ay = -12; Last.Sway = 8;
            }
            if (kind == FxKind.Complete) GroundRing(now, 36, 68, 0.75f, Theme.Accent, 0.7f, 1.5f);
        }

        // Deep-space stardust on a tilted orbital ring.
        void ChatGpt(FxKind kind, double now)
        {
            if (kind == FxKind.Mote)
            {
                Add(Shape.Orb, Curve.Bell, X(40, 60), Y(30, 45), R(-4, 4), -20, 1.2f, 6, A(245, 0xE0C3FC), A(200, 0x5B2BA0));
                return;
            }
            int stars = kind == FxKind.Complete ? 7 : kind == FxKind.Small ? 4 : kind == FxKind.Idle ? 2 : 0, orbs = kind == FxKind.Complete ? 5 : 0;
            if (kind == FxKind.Complete)
            {
                Add(Shape.Orbit, Curve.Linear, BoxW / 2, Y(47, 47), 0, 0, 0.85f, 64, A(170, 0x2AB3C6), A(230, 0x7C4DFF));
                Last.Rot = -14;
            }
            for (int i = 0; i < stars; i++)
            {
                Add(Shape.Twinkle, Curve.Bell, X(18, 82), Y(20, 58), R(35, 55), R(-32, -18), 0.85f, i % 2 == 0 ? 8 : 5, A(245, 0xE0C3FC), A(230, 0x7C4DFF));
                Last.Wave = 6;
            }
            for (int i = 0; i < orbs; i++) Add(Shape.Orb, Curve.Bell, X(18, 82), Y(20, 58), R(35, 55), R(-32, -18), 0.85f, 6, A(230, 0x7C4DFF), A(200, 0x5B2BA0));
        }

        // Pearl twin-prism ribbons rising in two mirrored columns, over counter-rotating ground arcs.
        void Gemini(FxKind kind, double now)
        {
            int prisms = kind == FxKind.Complete ? 6 : 0, glints = kind == FxKind.Complete ? 6 : kind == FxKind.Small ? 4 : kind == FxKind.Idle ? 1 : 0;
            for (int i = 0; i < prisms + glints; i++)
            {
                bool prism = i < prisms, left = i % 2 == 0;
                float x = left ? X(15, 32) : X(68, 85);
                if (prism) Add(Shape.Prism, Curve.PopHoldCubic, x, Y(14, 48), 0, -42, 0.8f, 12, A(245, 0xF3D9E5), A(230, 0xC4F1F9));
                else Add(Shape.TwinDot, Curve.PopHoldCubic, x, Y(14, 48), 0, -42, 0.8f, 5, A(230, 0xC4F1F9), A(210, 0xE8A5C8));
                Last.Ay = -8; Last.Sway = left ? 18 : -18; Last.Wave = 8;
            }
            if (kind == FxKind.Complete)
            {
                rings.Add(new Ring { Start = now, Life = 0.8, W0 = 52, W1 = 52, A0 = 0.8f, Stroke = 1.5f, Color = Theme.TieGemini, Spin = 240, Sweep = 110 });
                rings.Add(new Ring { Start = now, Life = 0.8, W0 = 44, W1 = 44, A0 = 0.7f, Stroke = 1.5f, Color = Theme.Hex(0xC4F1F9), Spin = -240, Sweep = 110 });
            }
        }

        // Monochrome horizon ring and kinetic slashes that snap out and freeze.
        void Grok(FxKind kind, double now)
        {
            int slashes = kind == FxKind.Complete ? 8 : kind == FxKind.Small ? 4 : kind == FxKind.Idle ? 2 : 0, sparks = kind == FxKind.Complete ? 4 : 0;
            if (kind == FxKind.Complete)
            {
                Add(Shape.Accretion, Curve.FlashSnap, BoxW / 2, Y(52, 52), 0, 0, 0.64f, 60, A(240, 0x111114), A(245, 0xECEFF4));
                Last.Rot = -10;
            }
            for (int i = 0; i < slashes; i++)
            {
                double angle = (i % 2 == 0 ? -35 : -145) + R(-12, 12) + (i % 4 >= 2 ? 70 : 0);
                double rad = angle * Math.PI / 180, speed = R(85, 110);
                Add(Shape.Slash, Curve.FlashSnap, X(20, 80), Y(10, 45), (float)(Math.Cos(rad) * speed), (float)(Math.Sin(rad) * speed), 0.64f, 9, A(245, 0xECEFF4), A(240, 0x111114));
                Last.Drag = 0.78f; Last.Rot = (float)angle;
            }
            for (int i = 0; i < sparks; i++)
            {
                double rad = R(0, 360) * Math.PI / 180;
                Add(Shape.Spark, Curve.FlashSnap, X(30, 70), Y(15, 40), (float)(Math.Cos(rad) * 60), (float)(Math.Sin(rad) * 60), 0.64f, 3, A(230, 0x2AB3C6), Color.Empty);
                Last.Drag = 0.78f;
            }
        }

        // ---- Simulation ----------------------------------------------------------------------------------

        public void Step(double now)
        {
            double dt = last < 0 ? 0 : Math.Min(0.1, now - last);
            last = now;
            for (int i = pending.Count - 1; i >= 0; i--)
                if (now >= pending[i].At) { Spawn(pending[i].Kind, now); pending.RemoveAt(i); }
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.Age += (float)dt;
                if (p.Age >= p.Life) { particles.RemoveAt(i); continue; }
                if (p.Drag < 1)
                {
                    float k = (float)Math.Pow(p.Drag, dt * 30);   // Gemini's per-frame drag, per second
                    p.Vx *= k; p.Vy *= k;
                }
                p.Vy += p.Ay * (float)dt;
                float vx = p.Vx;
                if (p.Sway != 0)
                    vx += p.Wave > 0 ? (float)(p.Sway * Math.Cos(p.Age * p.Wave + p.Phase)) : (float)(p.Sway * Math.Sin(p.Age * 6 + p.Phase));
                p.X += vx * (float)dt;
                p.Y += p.Vy * (float)dt;
                p.Rot += p.Spin * (float)dt;
            }
            rings.RemoveAll(r => now - r.Start >= r.Life);
        }

        // Ground rings join the pet's other rings under its shoes (sizes scaled to the pet).
        public void AddRings(Pose pose, double now, float size)
        {
            foreach (var r in rings)
            {
                double t = (now - r.Start) / r.Life;
                if (t < 0) continue;
                pose.Rings.Add(new GroundRing
                {
                    Width = (float)(r.W0 + (r.W1 - r.W0) * Theme.Decel(t)) * size,
                    Alpha = (float)(r.A0 * (1 - t)),
                    Stroke = r.Stroke * size,
                    Color = r.Color,
                    Start = r.Sweep > 0 ? (float)(r.Spin * (now - r.Start)) : 0,
                    Sweep = r.Sweep,
                });
            }
        }

        static float Fade(Particle p)
        {
            float t = p.Age / p.Life;
            switch (p.Curve)
            {
                case Curve.HoldThenSquare: return t < 0.25f ? 1 : (float)Math.Pow(1 - (t - 0.25f) / 0.75f, 2);
                case Curve.Bell: return (float)Math.Sin(Math.PI * t);
                case Curve.PopHoldCubic: return p.Age < 0.08f ? p.Age / 0.08f : t < 0.55f ? 1 : (float)Math.Pow(1 - (t - 0.55f) / 0.45f, 3);
                case Curve.FlashSnap: return t < 0.6f ? 1 : 1 - (t - 0.6f) / 0.4f;
                default: return 1 - t;
            }
        }

        // Draw over the figure; `box` is the drawn figure rectangle.
        public void Draw(Graphics g, RectangleF box)
        {
            if (particles.Count == 0) return;
            float k = box.Height / BoxH;
            foreach (var p in particles)
            {
                float a = Math.Max(0, Math.Min(1, Fade(p)));
                if (a <= 0.01f) continue;
                float x = box.X + p.X * k, y = box.Y + p.Y * k, s = p.Size * k, t = p.Age / p.Life;
                switch (p.Shape)
                {
                    case Shape.Star4: Star(g, x, y, s / 2, s / 2 * 0.32f, p.Rot, Fill(p.C1, a)); break;
                    case Shape.Twinkle:
                    {
                        float tw = (float)(0.875 + 0.275 * Math.Sin(p.Age * 2 * Math.PI * 6 + p.Phase));   // 0.6 ↔ 1.15 at 6 Hz
                        Star(g, x, y, s / 2 * tw, s / 2 * 0.25f * tw, 0, Fill(p.C2, a * 0.8f));
                        Star(g, x, y, s / 3 * tw, s / 3 * 0.25f * tw, 0, Fill(p.C1, a));
                        break;
                    }
                    case Shape.Diamond: Poly(g, Fill(p.C1, a), x, y - s * 0.6f, x + s * 0.45f, y, x, y + s * 0.6f, x - s * 0.45f, y); break;
                    case Shape.Orb:
                        using (var b = new SolidBrush(Fill(p.C2, a * 0.35f))) g.FillEllipse(b, x - s * 0.8f, y - s * 0.8f, s * 1.6f, s * 1.6f);
                        using (var b = new SolidBrush(Fill(p.C1, a))) g.FillEllipse(b, x - s * 0.4f, y - s * 0.4f, s * 0.8f, s * 0.8f);
                        break;
                    case Shape.Prism:
                    {
                        float sx = (float)(0.6 + 0.4 * Math.Cos(p.Age * 9 + p.Phase));   // ScaleX 1 → 0.2 → 1: a slow 3D spin
                        Poly(g, Fill(p.C1, a), x, y - s / 2, x + s / 4 * sx, y, x, y + s / 2, x - s / 4 * sx, y);
                        break;
                    }
                    case Shape.TwinDot:
                        using (var b = new SolidBrush(Fill(p.C1, a))) g.FillEllipse(b, x - 1.5f * k, y - 1.5f * k, 3 * k, 3 * k);
                        using (var b = new SolidBrush(Fill(p.C2, a))) g.FillEllipse(b, x + 3.5f * k, y + 2.5f * k, 5 * k, 5 * k);
                        break;
                    case Shape.Slash:
                    {
                        float width = t < 0.6f ? 3 * k : 3 * k * (1 - (t - 0.6f) / 0.4f);
                        var st = g.Save();
                        g.TranslateTransform(x, y);
                        g.RotateTransform(p.Rot);
                        using (var path = new GraphicsPath())
                        {
                            float h = width / 2, len = s / 2, shear = width * 0.7f;
                            path.AddPolygon(new[] { new PointF(-len + shear, -h), new PointF(len + shear, -h), new PointF(len - shear, h), new PointF(-len - shear, h) });
                            using (var b = new SolidBrush(Fill(p.C1, a))) g.FillPath(b, path);
                            using (var pen = new Pen(Fill(p.C2, a), Math.Max(0.6f, k * 0.8f))) g.DrawPath(pen, path);
                        }
                        g.Restore(st);
                        break;
                    }
                    case Shape.Spark: using (var b = new SolidBrush(Fill(p.C1, a))) g.FillEllipse(b, x - s / 2, y - s / 2, s, s); break;
                    case Shape.Orbit:
                    case Shape.Accretion:
                    {
                        var st = g.Save();
                        g.TranslateTransform(x, y);
                        g.RotateTransform(p.Rot);
                        float w = s, h = s * (p.Shape == Shape.Orbit ? 18f / 64 : 16f / 60);
                        float fade = p.Shape == Shape.Orbit ? (float)(1 - Math.Pow(t, 3)) : a;
                        float sweep = p.Shape == Shape.Orbit ? 360 * Math.Min(1, t * 2.2f) : 360;
                        if (p.Shape == Shape.Accretion)
                            using (var core = new Pen(Fill(p.C1, fade), 2 * k)) g.DrawEllipse(core, -w / 2, -h / 2, w, h);
                        using (var rim = new Pen(Fill(p.Shape == Shape.Orbit ? p.C1 : p.C2, fade), 1.5f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawArc(rim, -w / 2 - (p.Shape == Shape.Accretion ? 1.5f * k : 0), -h / 2 - (p.Shape == Shape.Accretion ? 1.5f * k : 0),
                                w + (p.Shape == Shape.Accretion ? 3 * k : 0), h + (p.Shape == Shape.Accretion ? 3 * k : 0), 160, sweep);
                        g.Restore(st);
                        break;
                    }
                }
            }
        }

        static Color Fill(Color c, float a) { return Color.FromArgb((int)(c.A * Math.Max(0, Math.Min(1, a))), c.R, c.G, c.B); }

        static void Star(Graphics g, float cx, float cy, float outer, float inner, float rotation, Color color)
        {
            var pts = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double ang = (rotation + i * 45 - 90) * Math.PI / 180;
                float r = i % 2 == 0 ? outer : inner;
                pts[i] = new PointF(cx + (float)(Math.Cos(ang) * r), cy + (float)(Math.Sin(ang) * r));
            }
            using (var b = new SolidBrush(color)) g.FillPolygon(b, pts);
        }

        static void Poly(Graphics g, Color color, params float[] xy)
        {
            var pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
            using (var b = new SolidBrush(color)) g.FillPolygon(b, pts);
        }

        // The Needs-you beacon's upper half (the ground radar rings live in Choreo): a warm glow behind the
        // raised hand and three sound arcs radiating from the ! badge. `phase` = seconds into the 0.8 s burst.
        public static void Beacon(Graphics g, RectangleF box, float phase, float badgeX, float badgeY, float size)
        {
            if (phase < 0 || phase > 0.8f) return;
            float k = box.Height / BoxH;
            float glow = (float)Math.Sin(Math.PI * phase / 0.8);
            float hx = box.X + 0.78f * box.Width, hy = box.Y + 0.26f * box.Height;
            for (int i = 3; i >= 1; i--)
            {
                float r = 12 * k * i / 3;
                using (var b = new SolidBrush(Color.FromArgb((int)(60 * glow), i == 1 ? Theme.Hex(0xFFF3D6) : Theme.Warning))) g.FillEllipse(b, hx - r, hy - r, r * 2, r * 2);
            }
            float travel = Math.Min(1, phase / 0.45f);
            for (int i = 0; i < 3; i++)
            {
                float d = (8 * travel + i * 4) * k, r = (6 + i * 3) * k;
                float alpha = (1 - travel) * (1 - i * 0.25f);
                if (alpha <= 0.02f) continue;
                double rad = -35 * Math.PI / 180;
                float cx = badgeX + (float)(Math.Cos(rad) * d), cy = badgeY + (float)(Math.Sin(rad) * d);
                using (var pen = new Pen(Color.FromArgb((int)(235 * alpha), i % 2 == 0 ? Theme.Warning : Theme.Accent), 1.5f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -35 - 30, 60);
            }
        }
    }
}
