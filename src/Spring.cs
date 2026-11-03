using System;

namespace OpaGachi
{
    // A spring token (Gemini Response 5): `Response` is the period in seconds, `Damping` the damping fraction
    // (1 = no overshoot), `Seconds` roughly when it's done (~99%). Layout, pages and reflow never bounce.
    struct Feel
    {
        public readonly double Response, Damping, Seconds;
        public Feel(double response, double damping, double seconds) { Response = response; Damping = damping; Seconds = seconds; }

        public static readonly Feel
            Snappy = new Feel(0.25, 0.85, 0.45),   // toggles, sliders, steppers
            Bouncy = new Feel(0.35, 0.65, 0.70),   // the desktop card opening, menus opening
            Smooth = new Feel(0.45, 1.00, 0.65),   // page switch, accordion, sidebar plate, pet re-flow
            Gentle = new Feel(0.60, 1.00, 0.90),   // Agents carousel, usage rings, footer line
            Settle = new Feel(0.35, 1.00, 0.50);   // closing menus and the card

        // Closed-form step response 0 → 1 after `seconds`, for one-shot motion that is never retargeted
        // (entrances, ring sweeps). Overshoots past 1 when Damping < 1.
        public double Progress(double seconds)
        {
            if (seconds <= 0) return 0;
            double w = 2 * Math.PI / Response, z = Damping;
            if (z >= 1) return 1 - (1 + w * seconds) * Math.Exp(-w * seconds);
            double wd = w * Math.Sqrt(1 - z * z);
            return 1 - Math.Exp(-z * w * seconds) * (Math.Cos(wd * seconds) + z / Math.Sqrt(1 - z * z) * Math.Sin(wd * seconds));
        }
    }

    // Damped harmonic oscillator, unit mass (Gemini Response 5). Moving `Target` mid-flight keeps the
    // velocity, so a reversed toggle or a re-opened card never jumps. Reduced motion lands on the target.
    sealed class Spring
    {
        public double Value, Target, Velocity;
        double stiffness, friction;

        public Spring(double value, Feel feel) { Value = Target = value; Tune(feel); }

        public void Tune(Feel feel)
        {
            stiffness = Math.Pow(2 * Math.PI / feel.Response, 2);
            friction = 4 * Math.PI * feel.Damping / feel.Response;
        }

        public bool AtRest { get { return Math.Abs(Velocity) < 0.01 && Math.Abs(Target - Value) < 0.01; } }

        public void Snap(double value) { Value = Target = value; Velocity = 0; }

        // Semi-implicit Euler in 4 sub-steps (stable at 30–64 fps timer jitter). True while still moving.
        public bool Step(double dt)
        {
            if (Theme.ReduceMotion || AtRest) { Snap(Target); return false; }
            double h = dt / 4;
            for (int i = 0; i < 4; i++)
            {
                Velocity += (stiffness * (Target - Value) - friction * Velocity) * h;
                Value += Velocity * h;
            }
            if (AtRest) { Snap(Target); return false; }
            return true;
        }
    }
}
