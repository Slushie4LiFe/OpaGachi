using System;
using System.Collections.Generic;

namespace OpaGachi
{
    // What the agent is doing. Each mode loops an animation; Thank, Fail and Idle Occasional play
    // once on top of the mode and then hand back to it.
    enum Mode { Idle, Bored, Think, Work, ImageGen, Help, Complete, FailedHold }

    enum Signal
    {
        Opened,        // agent appeared
        Think,         // thinking or researching
        Work,          // coding, creating, running commands
        ImageGen,      // generating an image
        NeedsApproval, // waiting on the user (permission, question)
        Approved,      // the user answered a NeedsApproval
        Denied,        // the user said no
        Error,         // an action failed, agent may carry on
        FatalError,    // the turn ended in failure; wait for the user to retry
        UserPrompted,  // the user sent a new message
        Done,          // finished the job
        Viewed,        // the user looked at the finished work
        Cancelled,     // the user stopped the turn
    }

    sealed class Pet
    {
        // Timings (seconds). Busy-to-busy changes settle first: a quick tool call doesn't interrupt a long
        // think and a short pause between tools doesn't interrupt work; the current loop just keeps playing
        // (user, 2026-10-31: Claude Code's hooks flip Work/Think every second or two).
        const double MinModeHold = 6;       // a busy mode stays at least this long before another busy mode replaces it
        const double SettleThink = 4;       // Think must be requested this long, uninterrupted, before it replaces Work
        const double SettleOther = 2.5;     // the same for Work / ImageGen replacing Think
        // Cross-fades between clips last about 250 ms whatever the clip's frame rate (3 frames at 12 fps, 6 at 24).
        int FadeFrames { get { return Math.Max(3, (int)Math.Round(0.25 * (player.Clip != null ? player.Clip.Fps : 12))); } }
        const int MaxQueued = 2;

        public readonly string AgentKey, InstanceKey;
        public readonly PetWindow Window;
        public string Name { get; private set; }
        public int Number;                       // 1 for the first pet of an agent, 2 for the next, ...
        public IntPtr HostWindow = IntPtr.Zero;  // the agent's window, for "user viewed the result"
        public int AgentPid;                     // the agent's process, to notice when it exits
        public double LastEvent;
        public bool HooksSeen;                   // Claude Code hooks report this session (they beat the transcript)
        public bool NeedsWindow, HasWindow;      // app pets leave once their window is closed and they're idle
        public double WindowGoneAt;
        public string Label;                     // where the pet comes from ("Gemini app", "Claude Code", ...)

        public Mode Mode { get; private set; }
        public double ModeSince { get { return modeSince; } }
        double modeSince;
        double completeShownAt = -1;      // when the Complete clip came on screen (Thank waits until it has shown a while)
        const double CompleteAtLeast = 3; // seconds of Complete before Thank may take over (user, 2026-11-01)
        public Action<Pet> Completed;     // PetManager: the completion sound
        Mode? pending;
        double pendingSince;

        // Playback. A clip is never cut off: one-shots (Thank, Fail, fidgets) play to their last frame;
        // a looping clip hands over at its last frame or at a rest frame, where the character is back in
        // its starting pose (rest.json). The badge and hop show a new state at once; the body follows.
        readonly List<Anim> queue = new List<Anim>();
        bool playingOneShot;
        int lastFrame;

        double idleSince, nextOccasional;
        public double BoredAfter, OccasionalEvery, OccasionalChance;

        readonly SpriteLibrary library;
        TieTint tint;
        readonly ClipPlayer player = new ClipPlayer();
        readonly Random random;
        Anim clipAnim;
        double clipStart;
        bool clipTinted;

        // Each mode picks one variant and keeps looping it until the mode changes; a fidget or other
        // one-shot in between returns to that same variant (user request, 2026-10-31).
        SpriteClip modeClip;
        bool modeClipTinted;
        Anim modeAnim;

        // Position in the row (driven by PetManager) and the choreography on top of it.
        public double X, Y, TargetX, TargetY;
        readonly Spring springX = new Spring(0, Feel.Smooth), springY = new Spring(0, Feel.Smooth);
        double moveStart, lastMotion;
        bool moving;
        public readonly Choreo Motion = new Choreo();
        public bool Closing;
        public int DrawnX = int.MinValue, DrawnY, DrawnOpacity;

        // Drawing scale set by PetManager: BaseScale maps clip pixels to screen pixels for the chosen
        // pet size; standing clips get StandingBoost on top.
        public static long DecodeTicks, DrawTicks;   // profiling: time spent decoding vs drawing frames
        public float BaseScale = 1f, StandingBoost;
        public bool ForceRedraw;      // redraw the current frame even if it hasn't advanced
        bool missedFrames;            // frames were skipped while hidden

        public Pet(string agentKey, string instanceKey, AgentInfo info, SpriteLibrary library, Random random, double now, int areaWidth, int areaHeight, bool interactive)
        {
            AgentKey = agentKey;
            InstanceKey = instanceKey;
            this.library = library;
            this.random = random;
            tint = MakeTint(info, library.Height);
            Motion.Tie = info != null && info.Tie.HasValue ? info.Tie.Value : Theme.Accent;
            Fx = new Effects(agentKey, random);
            Fx.Style = info == null ? null : info.SparkleColor ?? NameFill.Of(info, agentKey);
            Fx.Trigger = info == null ? "Both" : info.SparkleTrigger;
            Window = new PetWindow(areaWidth, areaHeight, interactive);
            Window.NameStyle = NameFill.Of(info, agentKey);
            Mode = Mode.Idle;
            modeSince = idleSince = LastEvent = now;
            nextOccasional = now + 4;
            Play(Anim.Idle, now, false);
            modeClip = player.Clip;
            modeClipTinted = clipTinted;
            modeAnim = Anim.Idle;
        }

        public void SetName(string name)
        {
            if (name == Name) return;
            Name = name;
            Window.SetName(name);
        }

        public Anim CurrentAnim { get { return clipAnim; } }

        // One or two words for the hover tag and the tray menu.
        public string StateWord
        {
            get
            {
                if (playingOneShot && clipAnim == Anim.Thank) return "Thanks!";
                switch (Mode)
                {
                    case Mode.Think: return "Thinking";
                    case Mode.Work: return "Working";
                    case Mode.ImageGen: return "Drawing";
                    case Mode.Help: return "Needs you";
                    case Mode.Complete: return "Done";
                    case Mode.FailedHold: return "Failed";
                    case Mode.Bored: return "Bored";
                    default: return "Idle";
                }
            }
        }
        public double ClipElapsed(double now) { return now - clipStart; }
        // The body shows the current mode (not a one-shot, not still finishing the previous state's clip).
        public bool ShowsMode { get { return !playingOneShot && queue.Count == 0 && clipAnim == AnimFor(Mode); } }
        public bool IsBusy { get { return Mode == Mode.Think || Mode == Mode.Work || Mode == Mode.ImageGen || Mode == Mode.Help; } }

        // Tie colour and optional pattern (agents.json "tie" / "texture"); null keeps the template's blue.
        public static TieTint MakeTint(AgentInfo info, int clipHeight)
        {
            if (info == null || (!info.Tie.HasValue && string.IsNullOrEmpty(info.Texture))) return null;
            string texture = string.IsNullOrEmpty(info.Texture) ? null : System.IO.Path.Combine(Program.Root, info.Texture);
            if (texture != null && !System.IO.File.Exists(texture)) texture = null;
            int tile = (int)Math.Round(clipHeight * 0.11);   // ~25 px on the 224 px clips: a few repeats on the tie
            return new TieTint(info.Tie.HasValue ? info.Tie.Value : System.Drawing.Color.White, texture, tile);
        }

        public void SetLook(AgentInfo info)
        {
            Fx.Style = info == null ? null : info.SparkleColor ?? NameFill.Of(info, AgentKey);
            Fx.Trigger = info == null ? "Both" : info.SparkleTrigger;
            tint = MakeTint(info, library.Height);
            Motion.Tie = info != null && info.Tie.HasValue ? info.Tie.Value : Theme.Accent;
            Window.NameStyle = NameFill.Of(info, AgentKey);
            ForceRedraw = true;
        }

        // Status bubble next to the head (assets/badges/<name>.png), or null for none.
        public string Badge
        {
            get
            {
                if (playingOneShot && (clipAnim == Anim.Thank || clipAnim == Anim.IdleOccasional)) return null;
                if ((playingOneShot && clipAnim == Anim.Fail) || Mode == Mode.FailedHold) return "fail";
                switch (Mode)
                {
                    case Mode.Help: return "help";
                    case Mode.Complete: return "complete";
                    case Mode.Think: return "think";
                    case Mode.Bored: return "bored";
                    default: return null;
                }
            }
        }

        // ---- Signals -------------------------------------------------------------------------

        public void Handle(Signal s, double now)
        {
            LastEvent = now;
            switch (s)
            {
                case Signal.Opened:
                    break;

                case Signal.Think:
                case Signal.Work:
                case Signal.ImageGen:
                    Busy(s == Signal.Think ? Mode.Think : s == Signal.Work ? Mode.Work : Mode.ImageGen, now);
                    break;

                case Signal.NeedsApproval:
                    SetMode(Mode.Help, now);
                    break;

                case Signal.Approved:
                    if (Mode == Mode.Help) ThankThen(Mode.Think, now);
                    break;

                case Signal.Denied:
                    if (Mode == Mode.Help) SetMode(Mode.Idle, now);
                    break;

                case Signal.Error:
                    Motion.Alert(AlertKind.Fail, now);
                    Enqueue(Anim.Fail);
                    break;

                case Signal.FatalError:
                    SetMode(Mode.FailedHold, now);
                    break;

                case Signal.UserPrompted:
                    if (Mode == Mode.Help || Mode == Mode.Complete || Mode == Mode.FailedHold) ThankThen(Mode.Think, now);
                    else SetMode(Mode.Think, now);
                    break;

                case Signal.Done:
                    SetMode(Mode == Mode.Help ? Mode.Idle : Mode.Complete, now);
                    break;

                case Signal.Viewed:
                    if (Mode == Mode.Complete && completeShownAt >= 0 && now - completeShownAt >= CompleteAtLeast) ThankThen(Mode.Idle, now);   // Complete always shows first
                    break;

                case Signal.Cancelled:
                    if (IsBusy) SetMode(Mode.Idle, now);
                    break;
            }
        }

        void Busy(Mode busy, double now)
        {
            if (Mode == Mode.Help)
            {
                ThankThen(busy, now);          // it got going again, so the request was approved
                return;
            }
            if (Mode == busy) { pending = null; return; }
            bool fromBusy = Mode == Mode.Think || Mode == Mode.Work || Mode == Mode.ImageGen;
            if (!fromBusy) { SetMode(busy, now); return; }
            if (pending != busy) { pending = busy; pendingSince = now; }
        }

        void ThankThen(Mode next, double now)
        {
            SetMode(next, now);
            Enqueue(Anim.Thank);
        }

        // ---- State ---------------------------------------------------------------------------

        void SetMode(Mode m, double now)
        {
            pending = null;
            if (m == Mode) return;
            Mode = m;
            modeSince = now;
            // Attention fires at once, before the body clip reaches its hand-over frame.
            if (m == Mode.Help) Motion.Alert(AlertKind.Help, now);
            else if (m == Mode.Complete)
            {
                completeShownAt = -1;
                Motion.Alert(AlertKind.Complete, now);
                Fx.Burst(FxKind.Complete, now, 0.08);   // at the hop's apex
                if (Completed != null) Completed(this);
            }
            else if (m == Mode.FailedHold) Motion.Alert(AlertKind.Fail, now);
            if (m == Mode.ImageGen) nextMote = now + 0.6;
            modeClip = null;   // the new mode picks its own variant
            if (m == Mode.Help)
            {
                // Needs you cuts in at once (user, 2026-11-01): no waiting for the current clip to end.
                queue.Clear();
                playingOneShot = false;
                PlayMode(now);
            }
            if (m == Mode.Idle)
            {
                idleSince = now;
                nextOccasional = now + OccasionalEvery;
            }
            else queue.Remove(Anim.IdleOccasional);   // a fidget that hasn't started yet no longer fits
        }

        // One-shots wait their turn; the same one is never queued twice.
        void Enqueue(Anim anim)
        {
            if (queue.Contains(anim)) return;
            if (playingOneShot && clipAnim == anim && anim != Anim.Thank) return;   // already showing it
            if (queue.Count >= MaxQueued) queue.RemoveAt(queue.Count - 1);
            queue.Add(anim);
        }

        static Anim AnimFor(Mode m) { return AnimSpec.ForMode(m); }

        // The choreography for this tick (hops, rings, hover, fades).
        public Pose Evaluate(double now)
        {
            var pose = Motion.Evaluate(now, Mode == Mode.Help, Mode == Mode.Idle || Mode == Mode.Think, lastFrame * 12 / Math.Max(1, player.Clip.Fps));   // the choreography counts 12 fps frames
            Fx.Step(now);
            Fx.AddRings(pose, now, Motion.Size);
            pose.Fx = Fx;
            if (Fx.Alive) pose.Animated = true;   // 30 fps only while particles live
            return pose;
        }

        // Per-character effects (Effects.cs).
        public readonly Effects Fx;
        double nextMote;

        // Advances the state machine and the animation. Returns true if the window needs redrawing.
        public bool Update(double now) { return Update(now, true, Evaluate(now)); }

        // `render` false (pet hidden): the state machine keeps running but no frames are decoded or drawn.
        // The window is redrawn when the clip advances or when the pose animates (hops, rings, hover glow).
        public bool Update(double now, bool render, Pose pose)
        {
            if (pending.HasValue && now - modeSince >= MinModeHold
                && now - pendingSince >= (pending == Mode.Think ? SettleThink : SettleOther))
                SetMode(pending.Value, now);

            if (AgentKey == "grok" && Mode == Mode.Complete && completeShownAt >= 0 && now - completeShownAt >= CompleteAtLeast) Handle(Signal.Viewed, now);
            if (Mode == Mode.Idle)
            {
                if (now - idleSince >= BoredAfter) SetMode(Mode.Bored, now);
                else if (now >= nextOccasional)
                {
                    nextOccasional = now + OccasionalEvery;
                    if (queue.Count == 0 && !playingOneShot && random.NextDouble() < OccasionalChance) Enqueue(Anim.IdleOccasional);
                    if (random.NextDouble() < 0.05) Fx.Burst(FxKind.Idle, now);   // a subtle sparkle now and then
                }
            }
            if (Mode == Mode.ImageGen && AgentKey == "chatgpt" && now >= nextMote)   // stardust rising while it draws
            {
                nextMote = now + 1.8;
                Fx.Burst(FxKind.Mote, now);
            }

            // Hand over to the next clip only at the end, or at a rest frame of a looping clip.
            var clip = player.Clip;
            if (library.Custom && Math.Abs(X - DrawnX) > 1 && Mode == Mode.Idle && clipAnim != Anim.Walk) { Play(Anim.Walk, now, true); clip = player.Clip; }
            int frame = (int)((now - clipStart) * clip.Fps);
            bool ended = frame >= clip.FrameCount;
            bool wantsChange = queue.Count > 0 || (!playingOneShot && clipAnim != AnimFor(Mode));
            bool justStarted = now - clipStart < 0.17;   // nothing visible to cut yet (e.g. a pet that has just appeared)
            if (ended || (wantsChange && !playingOneShot && (justStarted || clip.RestBetween(lastFrame, frame))))
            {
                NextClip(now);
                clip = player.Clip;
                frame = 0;
            }
            lastFrame = frame;
            if (Mode == Mode.Complete && completeShownAt < 0 && !playingOneShot && clipAnim == Anim.Complete) completeShownAt = now;

            if (!render)
            {
                missedFrames = true;
                return false;
            }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool advanced = player.SeekTo(frame);
            DecodeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            if (!advanced && !ForceRedraw && !missedFrames && !pose.Animated) return false;
            ForceRedraw = missedFrames = false;
            float scale = BaseScale * (player.Clip.Standing ? 1f + StandingBoost : 1f);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            Window.DrawFrame(player, clipTinted ? tint : null, scale, pose);
            DrawTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t1;
            return true;
        }

        void NextClip(double now)
        {
            if (queue.Count > 0)
            {
                var next = queue[0];
                queue.RemoveAt(0);
                playingOneShot = true;
                Play(next, now, true);
                if (next == Anim.Thank) Fx.Burst(FxKind.Complete, now, 0.1);   // the signature effect on Thank too
                return;
            }
            playingOneShot = false;
            PlayMode(now);
        }

        // The current mode's loop: the variant it already picked, or a fresh pick for a new mode.
        void PlayMode(double now)
        {
            var anim = AnimFor(Mode);
            if (modeClip != null && modeAnim == anim && !library.Custom)
            {
                Window.BeginFade(FadeFrames);
                if (clipAnim != anim) { Motion.Settle(now); Fx.Burst(FxKind.Shimmer, now); }
                clipAnim = anim;
                clipTinted = modeClipTinted;
                clipStart = now;
                lastFrame = 0;
                player.Start(modeClip);
                return;
            }
            Play(anim, now, true);
            modeClip = player.Clip;
            modeClipTinted = clipTinted;
            modeAnim = anim;
        }

        void Play(Anim anim, double now, bool fade)
        {
            bool tinted;
            var clip = library.Pick(AgentKey, anim, anim == clipAnim ? player.Clip : null, out tinted);
            if (fade && player.Clip != null) Window.BeginFade(FadeFrames);
            if (fade && player.Clip != null && anim != clipAnim) { Motion.Settle(now); Fx.Burst(FxKind.Shimmer, now); }   // a new pose settles 2 px behind a collar shimmer
            clipAnim = anim;
            clipTinted = tinted;
            clipStart = now;
            lastFrame = 0;
            player.Start(clip);
        }

        // The sprite set changed (custom sprites on or off): pick this mode's clip again.
        public void Repick(double now)
        {
            modeClip = null;
            if (!playingOneShot) PlayMode(now);
        }

        // Debug/demo menu: show an animation right now (once for one-shots, as the new mode for loops).
        public void Force(Anim anim, double now)
        {
            queue.Clear();
            modeClip = null;
            var spec = AnimSpec.Of(anim);
            if (spec.OneShot)
            {
                playingOneShot = true;
                Play(anim, now, true);
                return;
            }
            if (spec.Loops.HasValue) SetMode(spec.Loops.Value, now);
            playingOneShot = false;
            PlayMode(now);
        }

        // ---- Motion --------------------------------------------------------------------------

        // Glide to a new spot on the Smooth spring after `delay` (used to stagger a row; Gemini Response 5).
        // A pet already gliding just retargets, keeping its speed. Closing a gap sets off with a 3 px
        // step-hop. Under reduced motion the pet simply jumps there.
        public void MoveTo(double x, double y, double now, double delay, MoveCurve how)
        {
            if (moving ? (x == TargetX && y == TargetY) : (x == X && y == Y)) return;
            TargetX = x;
            TargetY = y;
            if (Theme.ReduceMotion) { Place(x, y); return; }
            springX.Target = x;
            springY.Target = y;
            if (moving) return;
            springX.Snap(X); springX.Target = x;
            springY.Snap(Y); springY.Target = y;
            moveStart = now + delay;
            moving = true;
            if (how == MoveCurve.CloseGap) Motion.GlideHop(moveStart);
        }

        // Put the pet straight on its spot (a newcomer: the arrival choreography does the rest).
        public void Place(double x, double y)
        {
            X = TargetX = x;
            Y = TargetY = y;
            springX.Snap(x);
            springY.Snap(y);
            moving = false;
        }

        public void StepMotion(double now)
        {
            double dt = Math.Min(0.05, Math.Max(0, now - lastMotion));
            lastMotion = now;
            if (!moving || now < moveStart) return;
            bool stepping = springX.Step(dt) | springY.Step(dt);
            X = springX.Value;
            Y = springY.Value;
            if (!stepping) { X = TargetX; Y = TargetY; moving = false; }
        }
    }

    enum MoveCurve { CloseGap, MakeRoom }
}
