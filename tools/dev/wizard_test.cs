using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

// Drives the real Setup wizard (installer\Setup.cs) through its buttons without ever showing it on screen: the form
// is created, laid out and run on a message loop while marked visible internally, then its button handlers are
// called. Covers Install, the Close button, Update, the Downgrade label and Cancel. Run by wizard_test.ps1, which
// isolates hooks and data with OPAGACHI_HOME and uninstalls afterwards. Captures go to tools\dev\captures\wizard-*.png.
namespace OpaGachi.Setup
{
    static class WizardTest
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OpaGachi";
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, finalBar;
        static string shots, finalStatus;

        static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL " + label); Console.WriteLine("PASS " + label); checks++; }
        static T Field<T>(Form f, string name) { return (T)typeof(SetupForm).GetField(name, Flags).GetValue(f); }
        static void Click(Control c) { typeof(Control).GetMethod("OnClick", Flags).Invoke(c, new object[] { EventArgs.Empty }); }
        static string Heading(Form f) { return f.Controls.OfType<Label>().First().Text; }

        static SetupForm Open(Stream payload)
        {
            var form = new SetupForm(payload);
            typeof(Control).GetMethod("SetState", Flags).Invoke(form, new object[] { 2, true });   // "visible" without a window on screen
            form.CreateControl();
            form.PerformLayout();
            return form;
        }

        static void Snap(Form f, string name)
        {
            using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size)); bmp.Save(Path.Combine(shots, name)); }
        }

        // Clicks the main button and pumps messages until the wizard says it's done, then clicks Close.
        static void InstallAndClose(SetupForm form, string shot)
        {
            var go = Field<Button>(form, "go");
            var ctx = new ApplicationContext();
            var timer = new Timer { Interval = 100 };
            int ticks = 0;
            timer.Tick += (s, e) =>
            {
                if (!(go.Text == "Close" && go.Enabled) && ++ticks < 1800) return;
                timer.Stop();
                finalStatus = Field<Label>(form, "status").Text;   // read before Close destroys the window
                finalBar = Field<ProgressBar>(form, "bar").Value;
                Snap(form, shot);
                Click(go);
                ctx.ExitThread();
            };
            Click(go);
            timer.Start();
            Application.Run(ctx);
        }

        [STAThread]
        static int Main(string[] args)
        {
            // Failures go to the console, never to a dialog on the user's screen.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            try { return Run(args); }
            catch (Exception e) { Console.WriteLine((e.InnerException ?? e).Message); return 2; }
        }

        static int Run(string[] args)
        {
            string dir = args[0];
            shots = args[1];
            Directory.CreateDirectory(shots);   // not in a fresh checkout
            Application.EnableVisualStyles();
            using (var key = Registry.CurrentUser.OpenSubKey(Key)) if (key != null) { Console.WriteLine("OpaGachi is installed; not testing over it."); return 1; }
            var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");

            var form = Open(payload);
            Check(Field<Button>(form, "go").Text == "Install" && Heading(form) == "Install OpaGachi " + Setup.Version, "Fresh wizard offers Install " + Setup.Version);
            Field<TextBox>(form, "folder").Text = dir;
            Field<CheckBox>(form, "desktop").Checked = false;   // keep the real desktop clean
            Field<CheckBox>(form, "launch").Checked = false;    // the dev copy is running; one instance at a time
            Snap(form, "wizard-1-install.png");
            InstallAndClose(form, "wizard-2-installed.png");
            Check(File.Exists(Path.Combine(dir, "OpaGachi.exe")) && File.Exists(Path.Combine(dir, "uninstall.exe")), "Install button installs into the chosen folder");
            Check(finalStatus == "OpaGachi " + Setup.Version + " is installed." && finalBar == 100, "Status line reports success with the bar at 100%");
            Check(form.IsDisposed, "Close button closes the wizard");
            Check(!File.Exists(Setup.DesktopLink), "Unticked desktop shortcut is not created");

            form = Open(payload);
            Check(Field<Button>(form, "go").Text == "Update" && Field<TextBox>(form, "folder").Text == dir, "Second run offers Update and finds the install folder");
            Snap(form, "wizard-3-update.png");
            Field<CheckBox>(form, "launch").Checked = false;
            File.WriteAllText(Path.Combine(dir, @"assets\stale.txt"), "old");
            InstallAndClose(form, "wizard-4-updated.png");
            Check(File.Exists(Path.Combine(dir, "OpaGachi.exe")) && !File.Exists(Path.Combine(dir, @"assets\stale.txt")), "Update button replaces the program files");

            using (var key = Registry.CurrentUser.OpenSubKey(Key, true)) key.SetValue("DisplayVersion", "9.9.9");
            form = Open(payload);
            Check(Field<Button>(form, "go").Text == "Downgrade" && Heading(form).Contains("with older"), "A newer install shows Downgrade, not Update");
            Snap(form, "wizard-5-downgrade.png");
            Click(Field<Button>(form, "cancel"));
            using (var key = Registry.CurrentUser.OpenSubKey(Key, true))
            {
                Check(form.IsDisposed && (string)key.GetValue("DisplayVersion") == "9.9.9", "Cancel closes without installing");
                key.SetValue("DisplayVersion", Setup.Version);
            }
            Console.WriteLine("TOTAL " + checks + " wizard checks passed");
            return 0;
        }
    }
}
