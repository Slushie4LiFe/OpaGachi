using System;
using System.Collections.Generic;
using System.Drawing;

namespace OpaGachi
{
    // A ring on the ground under a pet's shoes (Wii U Plaza style): an ellipse seen at a low angle.
    struct GroundRing
    {
        public float Width, Alpha, Stroke;
        public Color Color;
        public float Start, Sweep;   // a partial arc (degrees) when Sweep > 0, else the whole ellipse
    }

    // Where and how a pet is drawn this tick. Window values move the whole layered window (arrive, leave,
    // hide/show); figure values move the sprite inside it (hops, squash, shiver), so the ground decor
    // (shadow, rings, hover glow) stays planted while the figure moves.
    sealed class Pose
    {
        public float WinDy, Alpha = 1;
        public float Dx, Dy, Sx = 1, Sy = 1;
        public float ShadowWidth, ShadowAlpha;
        public float Glow;
        public Color GlowTint;
        public readonly List<GroundRing> Rings = new List<GroundRing>();
        public bool Animated;   // something besides the sprite frame changed: redraw this tick

        // Status badge (Badges.cs): which one, and its pop / swap / ambient motion.
        public string Badge, BadgeOld;
        public float BadgeScale, BadgeSx, BadgeSy, BadgeAlpha, BadgeDx, BadgeDy, BadgeAngle, BadgeMix, SymbolScale, CheckDraw, Glint;
        public bool SymbolOld;
        public int Frame;

        public Effects Fx;          // the pet's particles, drawn over the figure
        public float Beacon = -1;   // seconds into a Needs-you beacon burst (hand glow and sound arcs), or -1

        public void Reset()
        {
            WinDy = Dx = Dy = 0; Alpha = 1; Sx = Sy = 1; Glow = 0; ShadowWidth = ShadowAlpha = 0;
            Rings.Clear();
            Animated = false;
            Badge = BadgeOld = null;
            BadgeScale = BadgeSx = BadgeSy = BadgeAlpha = BadgeMix = SymbolScale = CheckDraw = 1;
            BadgeDx = BadgeDy = BadgeAngle = 0;
            Glint = -1;
            SymbolOld = false;
            Beacon = -1;
        }
    }

    enum AlertKind { None, Help, Complete, Fail, Greet }

    // Pet choreography from Gemini Response 3 (4.1a). Times in seconds; pixel values are for the Normal
    // size and scale with the pet. Under reduced motion only opacity changes remain.
    sealed class Choreo
    {
        public float Size = 1;              // pet size factor (spec values are for Normal)
        public Color Tie = Theme.Accent;    // ring tint; dark ties are lifted toward grey
        public bool Top;                    // row along the top of the screen: arrivals come from above

        double arriveAt = -100, arriveDelay;
        bool arriveFirst, arriving;
        double leaveAt = -100;
        bool leaveEnd, leaving;
        double visibleAt = -100, visibleFrom = 1;
        bool visible = true;
        AlertKind alert;
        double alertAt = -100, settleAt = -100, glideHopAt = -100, pressAt = -100, releaseAt = -100;
        bool pressed;
        double hover;
        readonly Pose pose = new Pose();

        static double Ease(double x1, double y1, double x2, double y2, double t) { return Theme.Bezier(x1, y1, x2, y2, t); }
        static double Clamp(double t) { return Theme.Clamp01(t); }
        static float Lerp(double a, double b, double t) { return (float)(a + (b - a) * t); }

        // ---- Triggers -------------------------------------------------------------------------------

        // Slides up into place (polished 2026-11-01, user): `delay` after its neighbours start making room, the pet
        // rises 30 px (24 when joining a row) on the Bouncy spring, fading in over 220 ms. Its body stretches with
        // its speed on the way up and squashes as it overshoots and settles; a tie-tinted ring spreads as it lands.
        public void Arrive(double now, bool first, double delay)
        {
            arriveAt = now; arriveDelay = delay; arriveFirst = first; arriving = true;
        }

        // End of the row: a 4 px anticipation lift, then sinks and shrinks. Middle: sinks over an imploding ring.
        public void Leave(double now, bool endOfRow)
        {
            if (leaving) return;
            leaveAt = now; leaveEnd = endOfRow; leaving = true;
        }

        public bool Gone(double now) { return leaving && now - leaveAt >= LeaveLength; }

        double LeaveLength { get { return Theme.ReduceMotion ? 0.12 : leaveEnd ? 0.36 : 0.32; } }

        // Own app in front: fade out 260 ms (sinking 6 px, 0.97 scale); back over 300 ms.
        public void SetVisible(bool on, double now)
        {
            if (on == visible) return;
            visibleFrom = VisibleAlpha(now);
            visible = on;
            visibleAt = now;
        }

        public bool Visible { get { return visible; } }

        public void Alert(AlertKind kind, double now) { alert = kind; alertAt = now; }

        // Badge changes: pop in (280 ms), squash-swap to another badge (280 ms), pop out (180 ms).
        string badge, badgeOld;
        double badgeAt = -100;
        int badgePhase;   // 0 settled, 1 popping in, 2 swapping, 3 popping out

        public void SetBadge(string name, double now)
        {
            if (!BadgeArt.Known(name)) name = null;
            if (name == badge) return;
            badgePhase = badge == null ? 1 : name == null ? 3 : 2;
            badgeOld = badge;
            badge = name;
            badgeAt = now;
        }
        public void Settle(double now) { settleAt = now; }                // body clip changed: a 2 px posture settle
        public void GlideHop(double start) { glideHopAt = start; }       // gliding along the row: a 3 px step-hop as it sets off
        public void Press(double now) { pressed = true; pressAt = now; autoRelease = -1; }
        public void Release(double now) { if (pressed) { pressed = false; releaseAt = now; } }
        public void Click(double now) { Press(now); autoRelease = now + 0.08; }   // scripted press + release (dev aid)
        double autoRelease = -1;
        public bool HoverOverride;   // scripted hover (dev aid: POST /signal {"hover": true})

        public void StepHover(bool hovered, double dt)
        {
            double old = hover, step = dt / (Theme.ReduceMotion ? 0.12 : 0.2);
            hover = hovered ? Math.Min(1, hover + step) : Math.Max(0, hover - step);
            if (hover != old) hoverMoving = true;
        }
        bool hoverMoving;

        public double Hover { get { return hover; } }

        double VisibleAlpha(double now)
        {
            double length = Theme.ReduceMotion ? 0.12 : visible ? 0.30 : 0.26;
            double k = Theme.Decel((now - visibleAt) / length);
            return visibleFrom + ((visible ? 1 : 0) - visibleFrom) * k;
        }

        // ---- Evaluation -------------------------------------------------------------------------------

        // needsYou: in Help mode (the beacon repeats every 2.4 s); calm: idle or thinking (the shadow
        // breathes with the sprite frame).
        public Pose Evaluate(double now, bool needsYou, bool calm, int frame)
        {
            var p = pose;
            p.Reset();
            if (pressed && autoRelease > 0 && now >= autoRelease) Release(autoRelease);
            float s = Size;
            bool reduced = Theme.ReduceMotion;
            double dir = Top ? -1 : 1;
            Color tie = Tie.GetBrightness() < 0.2f ? Theme.Mix(Tie, Theme.TextSecondary, 0.55) : Tie;

            // Arrival.
            if (arriving)
            {
                double t = now - arriveAt - arriveDelay;
                if (t < 0) p.Alpha = 0;
                else if (reduced) { p.Alpha = (float)Clamp(t / 0.15); arriving = t < 0.15; }
                else
                {
                    var feel = Feel.Bouncy;
                    double k = feel.Progress(t), speed = (feel.Progress(t + 0.01) - k) / 0.01;   // progress per second
                    p.WinDy = (float)(dir * (arriveFirst ? 30 : 24) * s * (1 - k));
                    p.Alpha = (float)Theme.Decel(t / 0.22);
                    float stretch = (float)Math.Max(-0.06, Math.Min(0.08, speed * 0.02));   // up to 8% taller rising, 6% squashed settling
                    p.Sy = 1 + stretch;
                    p.Sx = 1 - stretch * 0.6f;
                    double ring = t - 0.12;   // the feet reach the ground about here
                    if (ring >= 0 && ring < 0.42) p.Rings.Add(new GroundRing { Width = Lerp(8, arriveFirst ? 36 : 28, Theme.Decel(ring / 0.42)) * s, Alpha = Lerp(0.5, 0, ring / 0.42), Stroke = 2 * s, Color = tie });
                    p.Animated = true;
                    arriving = t < feel.Seconds;
                }
            }

            // Departure.
            if (leaving)
            {
                double t = now - leaveAt;
                if (reduced) p.Alpha *= (float)(1 - Clamp(t / 0.12));
                else if (leaveEnd)
                {
                    double u = Clamp(t / 0.36), anticipation = 0.09;
                    if (t < anticipation) p.WinDy += (float)(-4 * s * Theme.Decel(t / anticipation));
                    else p.WinDy += Lerp(-4 * s, 14 * s, Ease(0.4, 0, 0.85, 0.25, (t - anticipation) / (0.36 - anticipation)));
                    p.Alpha *= (float)(1 - Ease(0.4, 0, 0.85, 0.25, u));
                    p.Sx *= Lerp(1, 0.95, u); p.Sy *= Lerp(1, 0.92, u);
                    p.Animated = true;
                }
                else
                {
                    double u = Clamp(t / 0.32), k = Ease(0.4, 0, 0.7, 0.2, u);
                    p.WinDy += (float)(12 * s * k);
                    p.Alpha *= (float)(1 - k);
                    p.Rings.Add(new GroundRing { Width = Lerp(26, 6, Theme.Decel(u)) * s, Alpha = Lerp(0.35, 0, u), Stroke = 2 * s, Color = tie });
                    p.Animated = true;
                }
            }

            // Hide and show.
            double va = VisibleAlpha(now);
            p.Alpha *= (float)va;
            if (!reduced && va < 1)
            {
                p.WinDy += (float)(6 * s * (1 - va));
                float shrink = (float)(1 - 0.03 * (1 - va));
                p.Sx *= shrink; p.Sy *= shrink;
                if (now - visibleAt < 0.3) p.Animated = true;
            }

            if (!reduced)
            {
                EvaluateAlert(p, now, needsYou, s, tie);

                // Hover: lifts 2 px over a teal + tie glow on the ground.
                if (hover > 0)
                {
                    double h = Theme.Decel(hover);
                    p.Dy -= (float)(2 * s * h);
                    p.Glow = (float)(0.45 * h);
                    p.GlowTint = Theme.Mix(Theme.Accent, tie, 0.4);
                }
                if (hoverMoving) { p.Animated = true; hoverMoving = false; }

                // Click: press squash for 65 ms, then a pop with a teal ring on release.
                if (pressed)
                {
                    double k = Theme.Decel((now - pressAt) / 0.065);
                    p.Sx *= Lerp(1, 1.06, k); p.Sy *= Lerp(1, 0.93, k);
                    p.Dy += (float)(2 * s * k);
                    p.Animated = now - pressAt < 0.08;
                }
                else
                {
                    double t = now - releaseAt;
                    if (t < 0.24)
                    {
                        double u = Clamp(t / 0.175);
                        if (u < 0.35) { double k = Theme.EaseOut(u / 0.35); p.Sx *= Lerp(1.06, 0.97, k); p.Sy *= Lerp(0.93, 1.04, k); p.Dy += Lerp(2 * s, -4 * s, k); }
                        else { double k = Theme.Spring((u - 0.35) / 0.65); p.Sx *= Lerp(0.97, 1, k); p.Sy *= Lerp(1.04, 1, k); p.Dy += Lerp(-4 * s, 0, k); }
                        p.Rings.Add(new GroundRing { Width = Lerp(12, 42, Theme.Decel(t / 0.24)) * s, Alpha = Lerp(0.6, 0, t / 0.24), Stroke = 2 * s, Color = Theme.Accent });
                        p.Animated = true;
                    }
                }

                // A new body clip: settle 2 px into the pose.
                double st = now - settleAt;
                if (st < 0.25) { p.Dy += (float)(-2 * s * (1 - Theme.Decel(st / 0.25))); p.Animated = true; }

                // Gliding along the row: a 3 px step-hop as it sets off.
                double gt = now - glideHopAt;
                if (gt >= 0 && gt < 0.22) { p.Dy += (float)(-3 * s * Math.Sin(Math.PI * gt / 0.22)); p.Animated = true; }
            }

            EvaluateBadge(p, now, frame, reduced);

            // Soft contact shadow, 44 × 8 at Normal; breathes 44 ↔ 46 px with the sprite frame while calm and
            // shrinks as the figure leaves the ground.
            float breathe = calm && !reduced ? (float)(1 + Math.Sin(frame * Math.PI / 6)) : 0;
            float lift = Math.Max(0, -p.Dy) / (40 * s);
            p.ShadowWidth = (44 + breathe) * s * Math.Max(0.6f, 1 - lift) * p.Sx;
            p.ShadowAlpha = 0.32f * Math.Max(0.4f, 1 - lift * 1.5f);
            return p;
        }

        void EvaluateBadge(Pose p, double now, int frame, bool reduced)
        {
            p.Frame = frame;
            p.Badge = badge;
            double t = now - badgeAt;
            if (reduced)   // a 120 ms fade, nothing else
            {
                if (badgePhase == 3) { if (t < 0.12) { p.Badge = badgeOld; p.BadgeAlpha = (float)(1 - t / 0.12); p.Animated = true; } }
                else if (badgePhase != 0 && t < 0.12) { p.BadgeAlpha = (float)(t / 0.12); p.Animated = true; }
                return;
            }
            switch (badgePhase)
            {
                case 1:   // pop in: 0.4 → 1.18 → 1, rising 4 px, with a gloss sweep
                    if (t < 0.28)
                    {
                        double u = t / 0.28;
                        p.BadgeScale = u < 0.6 ? Lerp(0.4, 1.18, Theme.EaseOut(u / 0.6)) : Lerp(1.18, 1, Theme.EaseInOut((u - 0.6) / 0.4));
                        p.BadgeAlpha = (float)Clamp(u / 0.4);
                        p.BadgeDy = (float)(4 * (1 - Theme.Decel(u)));
                        p.Glint = (float)u;
                        p.Animated = true;
                    }
                    break;
                case 2:   // swap: squash to (1.12, 0.88) while the old glyph shrinks, then spring back with the new one
                    if (t < 0.28)
                    {
                        double ms = t * 1000;
                        if (ms < 90)
                        {
                            double k = ms / 90;
                            p.BadgeSx = Lerp(1, 1.12, k); p.BadgeSy = Lerp(1, 0.88, k);
                            p.SymbolOld = true; p.SymbolScale = Lerp(1, 0.5, k);
                        }
                        else
                        {
                            double k = (ms - 90) / 190;
                            if (k < 0.3) { double q = Theme.EaseOut(k / 0.3); p.BadgeSx = Lerp(1.12, 1.15, q); p.BadgeSy = Lerp(0.88, 1.15, q); }
                            else { double q = Theme.EaseInOut((k - 0.3) / 0.7); p.BadgeSx = p.BadgeSy = Lerp(1.15, 1, q); }
                            p.SymbolScale = Lerp(0.5, 1, Clamp(Theme.Spring(k)));
                        }
                        p.BadgeOld = badgeOld;
                        p.BadgeMix = (float)Clamp((ms - 90) / 140);
                        p.Animated = true;
                    }
                    break;
                case 3:   // pop out: shrink to 0.6 and fade, tucking 3 px back toward the head
                    if (t < 0.18)
                    {
                        double k = Ease(0.4, 0, 1, 1, t / 0.18);
                        p.Badge = badgeOld;
                        p.BadgeScale = Lerp(1, 0.6, k);
                        p.BadgeAlpha = (float)(1 - k);
                        p.BadgeDy = (float)(3 * k);
                        p.Animated = true;
                    }
                    break;
            }
            if (badgePhase == 1 || badgePhase == 2)
            {
                if (badge == "complete" && t < 0.52)   // the check draws itself, then one glint, then static
                {
                    p.CheckDraw = (float)Clamp((t - 0.1) / 0.18);
                    if (t >= 0.3) p.Glint = (float)((t - 0.3) / 0.22);
                    p.Animated = true;
                }
                if (badge == "fail" && t < 0.26)   // a little shake
                {
                    double[] keys = { 0, -2, 2, -1, 0 };
                    double pos = t / 0.26 * (keys.Length - 1);
                    int i = Math.Min(keys.Length - 2, (int)pos);
                    p.BadgeDx = (float)((keys[i] + (keys[i + 1] - keys[i]) * (pos - i)) * Size);
                    p.Animated = true;
                }
            }
            if (p.Badge == "help" && alert == AlertKind.Help)   // heartbeat and a ±6° wiggle with each beacon
            {
                double phase = (now - alertAt - 0.46) % 2.4;
                if (phase >= 0 && phase < 0.42)
                {
                    double q = phase / 0.42;
                    p.BadgeScale *= (float)(1 + 0.12 * Math.Sin(Math.PI * q));
                    p.BadgeAngle = (float)(6 * Math.Sin(2 * Math.PI * q));
                    p.Animated = true;
                }
            }
        }

        void EvaluateAlert(Pose p, double now, bool needsYou, float s, Color tie)
        {
            double t = now - alertAt;
            switch (alert)
            {
                case AlertKind.Help:
                    if (t < 0.46)
                    {
                        if (t < 0.26)
                        {
                            double u = t / 0.26;
                            p.Dy += (float)(-11 * s * Math.Sin(Math.PI * u));
                            // take-off squash (1.05, 0.94) → stretch at the apex (0.96, 1.04) → landing (1, 1)
                            if (u < 0.15) { double k = u / 0.15; p.Sx *= Lerp(1, 1.05, k); p.Sy *= Lerp(1, 0.94, k); }
                            else if (u < 0.5) { double k = Theme.EaseOut((u - 0.15) / 0.35); p.Sx *= Lerp(1.05, 0.96, k); p.Sy *= Lerp(0.94, 1.04, k); }
                            else { double k = Theme.EaseInOut((u - 0.5) / 0.5); p.Sx *= Lerp(0.96, 1, k); p.Sy *= Lerp(1.04, 1, k); }
                        }
                        else p.Dy += (float)(-5 * s * Math.Sin(Math.PI * (t - 0.26) / 0.2));
                        p.Rings.Add(new GroundRing { Width = Lerp(18, 44, Theme.Decel(t / 0.46)) * s, Alpha = Lerp(0.6, 0, t / 0.46), Stroke = 2 * s, Color = Theme.Warning });
                        p.Animated = true;
                    }
                    // The beacon: two staggered radar rings every 2.4 s until answered (sleeps in between).
                    if (needsYou && t >= 0.46)
                    {
                        double phase = (t - 0.46) % 2.4;
                        if (phase < 0.8) p.Beacon = (float)phase;
                        if (phase < 0.83)
                        {
                            for (int i = 0; i < 2; i++)
                            {
                                double q = (phase - i * 0.18) / 0.65;
                                if (q < 0 || q > 1) continue;
                                p.Rings.Add(new GroundRing { Width = Lerp(18, 46, Theme.Decel(q)) * s, Alpha = Lerp(0.8, 0, q), Stroke = 2 * s, Color = i == 0 ? Theme.Warning : Theme.Accent });
                            }
                            p.Animated = true;
                        }
                    }
                    break;
                case AlertKind.Complete:
                    if (t < 0.38)
                    {
                        double u = t / 0.38;
                        p.Dy += (float)(-10 * s * Math.Sin(Math.PI * Math.Min(1, u * 1.1)));
                        if (u < 0.2) { double k = u / 0.2; p.Sx *= Lerp(1, 1.04, k); p.Sy *= Lerp(1, 0.96, k); }
                        else if (u < 0.55) { double k = Theme.EaseOut((u - 0.2) / 0.35); p.Sx *= Lerp(1.04, 0.97, k); p.Sy *= Lerp(0.96, 1.03, k); }
                        else { double k = Theme.EaseInOut((u - 0.55) / 0.45); p.Sx *= Lerp(0.97, 1, k); p.Sy *= Lerp(1.03, 1, k); }
                        double r = Theme.Decel(u);
                        p.Rings.Add(new GroundRing { Width = Lerp(14, 38, r) * s, Alpha = Lerp(0.7, 0, u), Stroke = 2 * s, Color = Theme.Success });
                        p.Rings.Add(new GroundRing { Width = Lerp(8, 28, r) * s, Alpha = Lerp(0.6, 0, u), Stroke = 1.5f * s, Color = tie });
                        p.Animated = true;
                    }
                    break;
                case AlertKind.Greet:   // Welcome's Get started: every pet gives a little hop
                    if (t >= 0 && t < 0.36)
                    {
                        double u = t / 0.36;
                        p.Dy += (float)(-8 * s * Math.Sin(Math.PI * u));
                        if (u > 0.85) { double k = (u - 0.85) / 0.15; p.Sx *= Lerp(1.03, 1, k); p.Sy *= Lerp(0.97, 1, k); }
                        p.Animated = true;
                    }
                    else if (t < 0) p.Animated = true;
                    break;
                case AlertKind.Fail:
                    if (t < 0.42)
                    {
                        double u = t / 0.42;
                        double[] keys = { 0, -3, 3, -2, 2, 0 };
                        double pos = u * (keys.Length - 1);
                        int i = Math.Min(keys.Length - 2, (int)pos);
                        p.Dx += (float)(s * (keys[i] + (keys[i + 1] - keys[i]) * Theme.EaseInOut(pos - i)));
                        double slump = t < 0.08 ? t / 0.08 : t > 0.34 ? (0.42 - t) / 0.08 : 1;
                        p.Dy += (float)(2 * s * slump);
                        p.Rings.Add(new GroundRing { Width = Lerp(20, 36, Theme.Decel(u)) * s, Alpha = Lerp(0.5, 0, u), Stroke = 2 * s, Color = Theme.Danger });
                        p.Animated = true;
                    }
                    break;
            }
        }
    }
}
