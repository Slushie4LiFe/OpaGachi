// Self-check for src\Spring.cs. Build and run from the app folder:
//   csc /nologo /out:%TEMP%\spring_check.exe tools\dev\spring_check.cs src\Spring.cs && %TEMP%\spring_check.exe
using System;

namespace OpaGachi
{
    static class Theme { public static bool ReduceMotion; }   // stub: the real one lives in Theme.cs

    static class SpringCheck
    {
        static int failures;

        static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok    " : "FAIL  ") + what); if (!ok) failures++; }

        // Seconds a spring takes to rest from 0 → 1 at a 60 fps step, plus its peak value.
        static double Run(Feel feel, out double peak)
        {
            var s = new Spring(0, feel) { Target = 1 };
            double t = 0;
            peak = 0;
            while (s.Step(1 / 60.0) && t < 5) { t += 1 / 60.0; peak = Math.Max(peak, s.Value); }
            return t;
        }

        static int Main()
        {
            foreach (var pair in new[] { Tuple.Create("Snappy", Feel.Snappy), Tuple.Create("Bouncy", Feel.Bouncy), Tuple.Create("Smooth", Feel.Smooth),
                                         Tuple.Create("Gentle", Feel.Gentle), Tuple.Create("Settle", Feel.Settle) })
            {
                double peak, t = Run(pair.Item2, out peak);
                Console.WriteLine("      " + pair.Item1 + ": rests after " + t.ToString("0.00") + " s, peak " + peak.ToString("0.000"));
                Check(t > 0.2 && t < 1.5, pair.Item1 + " settles in a sensible time");
                Check(pair.Item2.Damping < 1 ? peak > 1.0 : peak <= 1.0005, pair.Item1 + (pair.Item2.Damping < 1 ? " overshoots" : " never overshoots"));
                Check(Math.Abs(pair.Item2.Progress(pair.Item2.Seconds) - 1) < 0.03, pair.Item1 + " closed form is ~done at its Seconds");
            }

            // Retarget mid-flight: no jump in value, velocity carried over.
            var r = new Spring(0, Feel.Smooth) { Target = 1 };
            for (int i = 0; i < 6; i++) r.Step(1 / 60.0);
            double v = r.Value, vel = r.Velocity;
            r.Target = 0;
            Check(r.Value == v && r.Velocity == vel, "retarget keeps value and velocity");
            r.Step(1 / 60.0);
            Check(Math.Abs(r.Value - v) < 0.1, "no jump after retarget");

            // Closed form agrees with the integrator.
            var c = new Spring(0, Feel.Bouncy) { Target = 1 };
            for (int i = 0; i < 12; i++) c.Step(1 / 60.0);
            Check(Math.Abs(c.Value - Feel.Bouncy.Progress(12 / 60.0)) < 0.02, "integrator matches the closed form");

            // Reduced motion snaps.
            Theme.ReduceMotion = true;
            var m = new Spring(0, Feel.Gentle) { Target = 5 };
            Check(!m.Step(1 / 60.0) && m.Value == 5 && m.Velocity == 0, "reduced motion lands on the target at once");

            Console.WriteLine(failures == 0 ? "all passed" : failures + " failed");
            return failures;
        }
    }
}
