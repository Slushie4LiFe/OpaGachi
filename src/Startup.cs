using Microsoft.Win32;

namespace OpaGachi
{
    // Run at Windows sign-in (Settings > Startup, user 2026-11-02): one value under the per-user Run key, so it
    // needs no admin rights and the uninstaller can remove it. Its argument is the mode: --background starts
    // quietly in the tray, --settings opens the Settings window.
    static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "OpaGachi";

        static string Command
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key == null ? null : key.GetValue(Name) as string; }
        }

        public static bool Enabled { get { return Command != null; } }
        public static bool Windowed { get { string c = Command; return c != null && c.Contains("--settings"); } }

        public static void Set(bool enabled, bool windowed)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (enabled) key.SetValue(Name, "\"" + Program.ExePath + "\" " + (windowed ? "--settings" : "--background"));
                else key.DeleteValue(Name, false);
            }
        }
    }
}
