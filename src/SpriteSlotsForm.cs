using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace OpaGachi
{
    sealed class SpriteSlotsForm : GlassForm
    {
        readonly PetManager pets;
        readonly FlowLayoutPanel cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        bool importing;
        readonly Anim? selected;
        public SpriteSlotsForm(PetManager manager, Anim? action = null)
        {
            pets = manager; selected = action; Text = "OpaGachi Animation slots"; ClientSize = new Size(650, 620);
            var done = new FlatButton("Done", 92) { Dock = DockStyle.Bottom };
            done.Click += (s,e) => Close(); Controls.Add(cards); Controls.Add(done);
            FormClosing += (s,e) => { if (importing) e.Cancel = true; };
            Rebuild();
        }
        void Rebuild()
        {
            foreach (Control c in cards.Controls.Cast<Control>().ToArray()) c.Dispose();
            var rules = new Label { AutoSize = true, MaximumSize = new Size(600, 0), Text = "Custom animation slots\nName files exactly ACTION_1 through ACTION_5 (uppercase). Maximum five per action. Use only PNGs OR only alpha WebMs across ALL actions. Clear all to change formats. WebMs: up to 10 seconds / 40 MB.", ForeColor = Theme.TextPrimary, BackColor = Color.Transparent, Padding = new Padding(14) };
            cards.Controls.Add(rules);
            foreach (var spec in AnimSpec.All.Where(s => !selected.HasValue || s.Anim == selected.Value))
            {
                var action = spec.Anim;
                string prefix = spec.Folder.ToUpperInvariant();
                var card = new GlassCard { Width = 608, Height = 76, AutoSize = false, Dock = DockStyle.None, Margin = new Padding(8,4,8,4) };
                string dir = Path.Combine(Path.Combine(pets.Library.Root, CustomSprite.Folder), spec.Folder);
                int count = Directory.Exists(dir) ? Directory.GetFiles(dir,"*.opaclip").Length : 0;
                // file stems get their own line so long ones (IDLE_OCCASIONAL) clear the button at Larger text
                var label = new Label { Text = Theme.Sentence(action.ToString()) + "   ·   " + count + " / 5 slots", AutoSize = true, Location = new Point(14,12), ForeColor = Theme.TextPrimary, BackColor = Color.Transparent };
                var add = new FlatButton("Add / replace...", 148) { Location = new Point(438,14) };
                var status = new Label { Text = prefix + "_1 … " + prefix + "_5", AutoSize = true, Location = new Point(14,37), ForeColor = Theme.TextSecondary, BackColor = Color.Transparent };
                add.Click += async (s,e) =>
                {
                    string formatFile = Path.Combine(Path.Combine(pets.Library.Root, CustomSprite.Folder), "format.txt");
                    string format = File.Exists(formatFile) ? File.ReadAllText(formatFile) : "";
                    string filter = format == ".png" ? "PNG (*.png)|*.png" : format == ".webm" ? "Alpha WebM (*.webm)|*.webm" : "Custom sprites (*.png;*.webm)|*.png;*.webm";
                    using (var dlg = new OpenFileDialog { Title = "Name files " + prefix + "_1 through " + prefix + "_5", Filter = filter })
                    {
                        if (dlg.ShowDialog(this) != DialogResult.OK) return;
                        importing = true; cards.Enabled = false;
                        try { await Task.Run(() => CustomSprite.ImportSlot(dlg.FileName, pets.Library.Root, pets.Library.Width, pets.Library.Height, action)); pets.RefreshSprites(); }
                        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Custom sprites", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                        finally { importing = false; cards.Enabled = true; Rebuild(); }
                    }
                };
                var body = new Panel { Width = 600, Height = 58, BackColor = Color.Transparent, Margin = new Padding(0) };
                body.Controls.Add(label); body.Controls.Add(status); body.Controls.Add(add); card.Controls.Add(body); cards.Controls.Add(card);
            }
            var clear = new FlatButton("Clear all custom sprites", 230) { Margin = new Padding(14) };
            clear.Click += (s,e) =>
            {
                if (MessageBox.Show(this,"Clear every custom animation and unlock the file format?", "Custom sprites",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning) != DialogResult.OK) return;
                string dir = Path.Combine(pets.Library.Root, CustomSprite.Folder);
                try { if (Directory.Exists(dir)) Directory.Delete(dir,true); pets.RefreshSprites(); Rebuild(); }
                catch(Exception ex) { MessageBox.Show(this, ex.Message); }
            };
            cards.Controls.Add(clear);
        }
    }
}
