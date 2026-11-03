using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("OpaGachi Setup")]

namespace OpaGachi.Setup
{
    // "OpaGachi Setup.exe": a per-user installer built by package.cmd with the C# compiler in Windows, so no
    // installer toolkit is needed. Built without the payload, the same code is the uninstall.exe it installs.
    //   OpaGachi Setup.exe                                   wizard; updates an existing install in place
    //   OpaGachi Setup.exe /silent [/dir=PATH] [/desktop] [/launch] [/force]   unattended install or update;
    //                                                        exit 2 = a newer version is installed (/force replaces it)
    //   uninstall.exe [/silent] [/purge]                     removes hooks, program files, shortcuts, startup and the Apps
    //                                                        entry; /purge (or Yes when asked) also deletes user data
    // User data lives in %LOCALAPPDATA%\OpaGachi, outside the install folder, so installs and updates never touch it.
    static class Setup
    {
        public const string App = "OpaGachi", Exe = "OpaGachi.exe", Uninstaller = "uninstall.exe";
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OpaGachi", RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static string Version { get { return Application.ProductVersion; } }
        public static string DefaultDir { get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), App); } }
        public static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), App + ".lnk"); } }
        static string MenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), App + ".lnk"); } }

        // Same rule as the app's Program.FindData (OPAGACHI_HOME stands in for the profile in tests).
        public static string DataDir
        {
            get
            {
                string home = Environment.GetEnvironmentVariable("OPAGACHI_HOME");
                string local = string.IsNullOrEmpty(home) ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : Path.Combine(Path.Combine(home, "AppData"), "Local");
                return Path.Combine(local, App);
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            bool silent = Has(args, "/silent");
            try
            {
                var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
                if (payload == null) return Uninstall(args, silent);
                using (payload)
                {
                    if (silent && NewerInstalled() && !Has(args, "/force")) return 2;   // never downgrade unattended
                    if (silent) Install(payload, Arg(args, "/dir=") ?? InstalledDir() ?? DefaultDir, Has(args, "/desktop") ? true : (bool?)null, Has(args, "/launch"), null);
                    else Application.Run(new SetupForm(payload));
                }
                return 0;
            }
            catch (Exception e)
            {
                if (!silent) MessageBox.Show(e.Message, App + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // desktop: true adds the shortcut, false removes it, null leaves it as it is.
        public static void Install(Stream payload, string dir, bool? desktop, bool launch, Action<int, string> progress)
        {
            dir = Path.GetFullPath(dir).TrimEnd('\\');
            if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() && !File.Exists(Path.Combine(dir, Exe)))
                throw new IOException(dir + " already has other files in it. Choose an empty folder, or the folder OpaGachi is installed in.");
            Report(progress, 0, "Closing " + App + "...");
            StopApp(dir);
            // The install folder holds only what Setup puts there, so an update clears the old assets first and
            // files dropped from a release don't linger.
            string assets = Path.Combine(dir, "assets");
            if (Directory.Exists(assets)) Retry(() => Directory.Delete(assets, true));
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read, true))   // left open so the wizard can retry
            {
                int done = 0, total = zip.Entries.Count;
                foreach (var entry in zip.Entries)
                {
                    string dest = Path.GetFullPath(Path.Combine(dir, entry.FullName.Replace('/', '\\')));
                    if (!dest.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected path in the setup payload: " + entry.FullName);
                    if (entry.Name.Length == 0) Directory.CreateDirectory(dest);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        var item = entry;
                        Retry(() => item.ExtractToFile(dest, true));   // agent hooks run the exe for a moment now and then
                    }
                    Report(progress, ++done * 95 / total, "Copying " + entry.FullName);
                }
            }
            string exe = Path.Combine(dir, Exe);
            Shortcut(MenuLink, exe);
            if (desktop == true) Shortcut(DesktopLink, exe);
            else if (desktop == false && File.Exists(DesktopLink)) File.Delete(DesktopLink);
            Register(dir);
            Report(progress, 100, App + " " + Version + " is installed.");
            if (launch) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir });
        }

        static int Uninstall(string[] args, bool silent)
        {
            string self = Application.ExecutablePath, dir = Arg(args, "/dir=") ?? Path.GetDirectoryName(self);
            if (!Has(args, "/fromtemp"))
            {
                // A running exe can't delete itself or its folder: hand over to a copy in %TEMP%.
                string temp = Path.Combine(Path.GetTempPath(), App + "-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(self, temp);
                Process.Start(temp, string.Join(" ", args.Where(a => !a.StartsWith("/dir=")).Select(Quote)) + " /fromtemp " + Quote("/dir=" + dir));
                return 0;
            }
            try
            {
                if (!File.Exists(Path.Combine(dir, Exe))) throw new IOException(App + " isn't installed in " + dir + ".");
                if (!silent && MessageBox.Show("Uninstall " + App + "?", App, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
                bool purge = Has(args, "/purge") || (!silent && Directory.Exists(DataDir) && MessageBox.Show(
                    "Also delete your settings, custom sprites, uploaded ties and sounds?\n\n" + DataDir, App,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
                StopApp(dir);
                // Agent hooks run this exe; take them out first so Claude Code, Codex, Gemini CLI and Grok carry on normally.
                using (var hooks = Process.Start(new ProcessStartInfo(Path.Combine(dir, Exe), "--remove-hooks") { UseShellExecute = false }))
                    hooks.WaitForExit(30000);
                foreach (var link in new[] { MenuLink, DesktopLink })
                    if (File.Exists(link) && LinkTarget(link).StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)) File.Delete(link);
                Retry(() => Directory.Delete(dir, true));
                using (var key = Registry.CurrentUser.OpenSubKey(Key))
                    if (key != null && string.Equals(key.GetValue("InstallLocation") as string, dir, StringComparison.OrdinalIgnoreCase)) Registry.CurrentUser.DeleteSubKeyTree(Key, false);
                using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))   // Settings > Startup > Run at startup
                    if (run != null && (run.GetValue(App) as string ?? "").IndexOf(dir + "\\", StringComparison.OrdinalIgnoreCase) >= 0) run.DeleteValue(App, false);
                if (purge && Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
                if (!silent) MessageBox.Show(App + " was uninstalled.", App, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            finally
            {
                // This temporary copy deletes itself once it has exited.
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /q " + Quote(self)) { CreateNoWindow = true, UseShellExecute = false });
            }
        }

        public static string InstalledDir()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Key))
            {
                string dir = key == null ? null : key.GetValue("InstallLocation") as string;
                return dir != null && File.Exists(Path.Combine(dir, Exe)) ? dir : null;
            }
        }

        public static string InstalledVersion()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Key))
                return key == null ? null : key.GetValue("DisplayVersion") as string;
        }

        // A newer OpaGachi is installed than this Setup carries (semantic versions compare as System.Version).
        public static bool NewerInstalled()
        {
            System.Version installed, mine;
            return InstalledDir() != null && System.Version.TryParse(InstalledVersion() ?? "", out installed)
                && System.Version.TryParse(Version, out mine) && installed > mine;
        }

        static void Register(string dir)
        {
            long bytes = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            {
                key.SetValue("DisplayName", App);
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "Slushie4LiFe");
                key.SetValue("URLInfoAbout", "https://Slushie4LiFe.com");
                key.SetValue("InstallLocation", dir);
                key.SetValue("DisplayIcon", Path.Combine(dir, Exe));
                key.SetValue("UninstallString", Quote(Path.Combine(dir, Uninstaller)));
                key.SetValue("QuietUninstallString", Quote(Path.Combine(dir, Uninstaller)) + " /silent");
                key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        static void StopApp(string dir)
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Exe)))
                using (p)
                {
                    try { if (p.MainModule.FileName.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(5000); } }
                    catch (Exception) { }   // already gone, or not one we can inspect
                }
        }

        static void Shortcut(string link, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link));
            var type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            try
            {
                object lnk = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                Set(lnk, "TargetPath", target);
                Set(lnk, "WorkingDirectory", Path.GetDirectoryName(target));
                Set(lnk, "IconLocation", target + ",0");
                Set(lnk, "Description", "Desk pets that act out what your AI agents are doing");
                lnk.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                Marshal.ReleaseComObject(lnk);
            }
            finally { Marshal.ReleaseComObject(shell); }
        }

        static string LinkTarget(string link)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            try
            {
                object lnk = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                string target = lnk.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null) as string;
                Marshal.ReleaseComObject(lnk);
                return target ?? "";
            }
            finally { Marshal.ReleaseComObject(shell); }
        }

        static void Set(object target, string name, object value) { target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value }); }

        // Files can be locked for a moment (an agent hook running the exe, antivirus scanning a new file).
        static void Retry(Action action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { action(); return; }
                catch (IOException) { if (attempt >= 40) throw; }
                catch (UnauthorizedAccessException) { if (attempt >= 40) throw; }
                Thread.Sleep(250);
            }
        }

        static void Report(Action<int, string> progress, int percent, string text) { if (progress != null) progress(percent, text); }
        static bool Has(string[] args, string flag) { return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)); }
        static string Arg(string[] args, string prefix) { var a = args.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)); return a == null ? null : a.Substring(prefix.Length).Trim('"'); }
        static string Quote(string s) { return "\"" + s + "\""; }
    }

    sealed class SetupForm : Form
    {
        readonly TextBox folder = new TextBox();
        readonly CheckBox desktop = new CheckBox { Text = "Create a desktop shortcut", AutoSize = true };
        readonly CheckBox launch = new CheckBox { Text = "Start OpaGachi when setup finishes", AutoSize = true, Checked = true };
        readonly Button browse = new Button { Text = "Browse..." }, go = new Button(), cancel = new Button { Text = "Cancel" };
        readonly ProgressBar bar = new ProgressBar();
        readonly Label status = new Label { AutoEllipsis = true };
        bool finished;

        public SetupForm(Stream payload)
        {
            string installed = Setup.InstalledDir(), version = Setup.InstalledVersion();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = Setup.App + " " + Setup.Version + " Setup";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 268);

            bool older = Setup.NewerInstalled();
            Controls.Add(new Label { Text = installed == null ? "Install OpaGachi " + Setup.Version : older ? "Replace OpaGachi " + version + " with older " + Setup.Version : "Update OpaGachi " + (version ?? "") + " to " + Setup.Version, Font = new Font("Segoe UI Semibold", 13F), AutoSize = true, Location = new Point(18, 14) });
            Controls.Add(new Label { Text = "Desk pets that act out what your AI agents are doing. Your settings and anything you add are kept in " + Setup.DataDir + ", so updates never touch them.", Location = new Point(20, 48), Size = new Size(480, 36), AutoEllipsis = true });
            Controls.Add(new Label { Text = "Install folder", AutoSize = true, Location = new Point(20, 94) });
            folder.Text = installed ?? Setup.DefaultDir;
            folder.SetBounds(20, 114, 384, 24);
            browse.SetBounds(412, 113, 88, 26);
            desktop.Location = new Point(20, 148);
            desktop.Checked = installed == null || File.Exists(Setup.DesktopLink);
            launch.Location = new Point(20, 172);
            bar.SetBounds(20, 204, 480, 10);
            status.SetBounds(20, 236, 296, 20);
            go.Text = installed == null ? "Install" : older ? "Downgrade" : "Update";
            go.SetBounds(324, 230, 84, 28);
            cancel.SetBounds(416, 230, 84, 28);
            Controls.AddRange(new Control[] { folder, browse, desktop, launch, bar, status, go, cancel });
            AcceptButton = go;
            CancelButton = cancel;

            browse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { Description = "Choose where to install OpaGachi", SelectedPath = Path.GetDirectoryName(folder.Text) ?? "" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    string path = dlg.SelectedPath;
                    if (!string.Equals(Path.GetFileName(path), Setup.App, StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, Setup.App);   // never spill into a shared folder
                    folder.Text = path;
                }
            };
            cancel.Click += (s, e) => Close();
            go.Click += async (s, e) =>
            {
                if (finished) { Close(); return; }
                foreach (Control c in new Control[] { folder, browse, desktop, launch, go, cancel }) c.Enabled = false;
                try
                {
                    string dir = folder.Text;
                    bool shortcut = desktop.Checked, start = launch.Checked;
                    await Task.Run(() => Setup.Install(payload, dir, shortcut, start, (percent, text) => BeginInvoke((Action)(() => Progress(percent, text)))));
                    finished = true;
                    go.Text = "Close";
                    go.Enabled = true;
                }
                catch (Exception ex)
                {
                    status.Text = "Setup didn't finish.";
                    MessageBox.Show(this, ex.Message, Setup.App + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    foreach (Control c in new Control[] { folder, browse, desktop, launch, go, cancel }) c.Enabled = true;
                }
            };
            FormClosing += (s, e) => { if (!go.Enabled && !cancel.Enabled) e.Cancel = true; };   // mid-install
        }

        // Windows animates a progress bar moving up but not down, so overshoot by one and step back: the bar then
        // shows the real progress (it lagged far behind "is installed" without this).
        void Progress(int percent, string text)
        {
            bar.Maximum = 101;
            bar.Value = percent + 1;
            bar.Value = percent;
            bar.Maximum = 100;
            status.Text = text;
        }
    }
}
