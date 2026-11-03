using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpaGachi
{
    enum Anim { Walk, Idle, IdleOccasional, Bored, Think, Work, ImageGen, Help, Fail, Complete, Thank }

    // Every animation in one place (user, 2026-11-02: easy to add more). Each has a folder of clips,
    // assets/sprites/<agent or _template>/<folder>/*.opaclip, where every clip is a variant picked at random, and
    // either the mode it loops for or "plays once". So:
    //  * a new variant for any state needs no code: put the .webm in ..\1TEMPLATES\<FOLDER> and run
    //    build-assets.cmd (or drop a built .opaclip into the folder);
    //  * a new animation is one Anim value plus one line here (and a Mode, if it's a new state).
    // Clips may have any frame rate; rest frames (rest.json) are optional and only let loops hand over early.
    sealed class AnimSpec
    {
        public readonly Anim Anim;
        public readonly string Folder;
        public readonly Mode? Loops;    // the mode this clip loops for, if any
        public readonly bool OneShot;   // plays once, to its last frame (also how the demo menu shows it)

        AnimSpec(Anim anim, string folder, Mode? loops, bool oneShot) { Anim = anim; Folder = folder; Loops = loops; OneShot = oneShot; }

        public static readonly AnimSpec[] All =
        {
            new AnimSpec(Anim.Walk, "walk", null, true),
            new AnimSpec(Anim.Idle, "idle", Mode.Idle, false),
            new AnimSpec(Anim.IdleOccasional, "idle_occasional", null, true),
            new AnimSpec(Anim.Bored, "bored", Mode.Bored, false),
            new AnimSpec(Anim.Think, "think", Mode.Think, false),
            new AnimSpec(Anim.Work, "work", Mode.Work, false),
            new AnimSpec(Anim.ImageGen, "imagegen", Mode.ImageGen, false),
            new AnimSpec(Anim.Help, "help", Mode.Help, false),
            new AnimSpec(Anim.Fail, "fail", Mode.FailedHold, true),   // once after an error; loops while a failure holds
            new AnimSpec(Anim.Complete, "complete", Mode.Complete, false),
            new AnimSpec(Anim.Thank, "thank", null, true),
        };

        public static AnimSpec Of(Anim anim) { return All.First(s => s.Anim == anim); }

        // The clip a mode loops (Idle if none is listed).
        public static Anim ForMode(Mode mode)
        {
            var spec = All.FirstOrDefault(s => s.Loops == mode);
            return spec != null ? spec.Anim : Anim.Idle;
        }
    }

    // Finds clips on disk and keeps recently used ones in memory.
    // Layout: assets/sprites/<agent>/<animation>/*.opaclip, falling back to assets/sprites/_template.
    // Template clips get the agent's tie colour at runtime; an agent's own clips are used as drawn.
    sealed class SpriteLibrary
    {
        public const string Template = "_template";
        const int CacheLimit = 6;   // ~3 MB per clip at the 224 px base size

        readonly string root;
        readonly Dictionary<string, string[]> listings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, SpriteClip> cache = new Dictionary<string, SpriteClip>(StringComparer.OrdinalIgnoreCase);
        readonly LinkedList<string> recent = new LinkedList<string>();
        readonly Random random = new Random();

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Evictions;   // clips dropped from the cache (their ~3 MB arrays live on the large object heap)
        public string Root { get { return root; } }

        // Experimental custom sprites (Settings → Experimental): every agent and animation shows the one custom
        // clip, or the template without tie colours until a file has been chosen.
        public bool Custom;
        public bool HasCustom { get { return AnimSpec.All.Any(s => List(CustomSprite.Folder, s.Anim).Length > 0); } }

        // Forget what's on disk (a custom sprite was just imported).
        public void Reload()
        {
            listings.Clear();
            cache.Clear();
            recent.Clear();
        }

        public SpriteLibrary(string spritesRoot)
        {
            root = spritesRoot;
            var any = List(Template, Anim.Idle).FirstOrDefault();
            if (any == null) throw new FileNotFoundException("No idle clips found in " + Path.Combine(root, Template, "idle") + ". Run build-assets.cmd first.");
            var probe = Get(any);
            Width = probe.Width;
            Height = probe.Height;
        }

        // Picks a clip for the animation, avoiding `previous` when there is a choice.
        // `tinted` is false when the agent has its own drawn clips for this animation.
        public SpriteClip Pick(string agent, Anim anim, SpriteClip previous, out bool tinted)
        {
            tinted = false;
            if (Custom)
            {
                var custom = List(CustomSprite.Folder, anim);
                if (custom.Length == 0) custom = List(CustomSprite.Folder, Anim.Idle);
                if (custom.Length > 0)
                {
                    var others = custom.Where(f => previous == null || f != previous.Path).ToArray();
                    return Get(others.Length > 0 ? others[random.Next(others.Length)] : custom[0]);
                }
                agent = Template;
            }
            var files = List(agent, anim);
            if (files.Length == 0 || agent == Template)
            {
                files = List(Template, anim);
                tinted = !Custom;
            }
            if (files.Length == 0)
            {
                files = List(Template, Anim.Idle);  // missing animation: never leave a pet blank
                tinted = !Custom;
            }
            string choice = files[0];
            if (files.Length > 1)
            {
                var others = previous == null ? files : files.Where(f => !string.Equals(f, previous.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
                choice = others[random.Next(others.Length)];
            }
            return Get(choice);
        }

        string[] List(string agent, Anim anim)
        {
            string key = agent + "/" + AnimSpec.Of(anim).Folder;
            string[] files;
            if (!listings.TryGetValue(key, out files))
            {
                string dir = Path.Combine(Path.Combine(root, agent), AnimSpec.Of(anim).Folder);
                files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.opaclip").OrderBy(f => f).ToArray() : new string[0];
                listings[key] = files;
            }
            return files;
        }

        SpriteClip Get(string path)
        {
            SpriteClip clip;
            if (cache.TryGetValue(path, out clip))
            {
                recent.Remove(path);
                recent.AddFirst(path);
                return clip;
            }
            clip = SpriteClip.Load(path);
            clip.Standing = IsStanding(path);
            clip.RestFrames = RestFrames(path);
            cache[path] = clip;
            recent.AddFirst(path);
            while (recent.Count > CacheLimit)
            {
                cache.Remove(recent.Last.Value);  // players already holding it keep their reference
                recent.RemoveLast();
                Evictions++;
            }
            return clip;
        }

        // poses.json in each sprite folder: {"standing": ["idle", "imagegen/imagegen2", ...]}
        readonly Dictionary<string, HashSet<string>> standing = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // rest.json in each sprite folder (tools/find_rest_frames.py): {"idle/idle_1": [96], ...}
        readonly Dictionary<string, IDictionary<string, object>> rest = new Dictionary<string, IDictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        int[] RestFrames(string clipPath)
        {
            string animDir = Path.GetDirectoryName(clipPath);
            string agentDir = Path.GetDirectoryName(animDir);
            IDictionary<string, object> json;
            if (!rest.TryGetValue(agentDir, out json))
            {
                json = Json.ReadFile(Path.Combine(agentDir, "rest.json"));
                rest[agentDir] = json;
            }
            object list;
            string key = Path.GetFileName(animDir) + "/" + Path.GetFileNameWithoutExtension(clipPath);
            if (json == null || !json.TryGetValue(key, out list) || !(list is object[])) return new int[0];
            return ((object[])list).Select(v => Convert.ToInt32(v)).ToArray();
        }

        bool IsStanding(string clipPath)
        {
            string animDir = Path.GetDirectoryName(clipPath);
            string agentDir = Path.GetDirectoryName(animDir);
            HashSet<string> set;
            if (!standing.TryGetValue(agentDir, out set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var json = Json.ReadFile(Path.Combine(agentDir, "poses.json"));
                object list;
                if (json != null && json.TryGetValue("standing", out list) && list is object[])
                    foreach (var entry in (object[])list) if (entry is string) set.Add(((string)entry).Replace('\\', '/'));
                standing[agentDir] = set;
            }
            string anim = Path.GetFileName(animDir);
            return set.Contains(anim) || set.Contains(anim + "/" + Path.GetFileNameWithoutExtension(clipPath));
        }
    }
}
