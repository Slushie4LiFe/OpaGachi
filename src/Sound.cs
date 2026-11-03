using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace OpaGachi
{
    // Completion sounds (user, 2026-11-01): the bundled ones in assets/sounds (from Pixabay) plus any the user
    // adds (kept in their data folder so updates don't remove them), played through Windows' MCI so MP3 works
    // with nothing extra installed.
    static class Sound
    {
        static string Stock { get { return Path.Combine(Path.Combine(Program.Root, "assets"), "sounds"); } }
        public static string Folder { get { return Path.Combine(Program.Data, "sounds"); } }

        // Every sound's file name, alphabetical ("Comet.mp3", ...).
        public static string[] All()
        {
            return new[] { Stock, Folder }.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d)).Where(IsAudio)
                .Select(f => Path.GetFileName(f)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static bool IsAudio(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mp3" || ext == ".wav";
        }

        // "Comet" for "Comet.mp3".
        public static string Label(string file) { return Path.GetFileNameWithoutExtension(file); }

        // Copies the user's own file in (keeping its name) and returns that name.
        public static string Add(string source)
        {
            if (!IsAudio(source)) throw new InvalidDataException("Choose an MP3 or WAV file.");
            Directory.CreateDirectory(Folder);
            string name = Path.GetFileName(source).Trim();
            File.Copy(source, Path.Combine(Folder, name), true);
            return name;
        }

        public static void Play(string file)
        {
            string name = Path.GetFileName(file ?? ""), path = Path.Combine(Folder, name);
            if (!File.Exists(path)) path = Path.Combine(Stock, name);
            if (!File.Exists(path)) { Log.Write("sound: missing " + name); return; }
            mciSendString("close opagachi", null, 0, IntPtr.Zero);
            int err = mciSendString("open \"" + path + "\" type mpegvideo alias opagachi", null, 0, IntPtr.Zero);
            if (err == 0) err = mciSendString("play opagachi", null, 0, IntPtr.Zero);
            if (err != 0) Log.Write("sound: MCI error " + err + " for " + path);
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int mciSendString(string command, StringBuilder buffer, int size, IntPtr callback);
    }
}
