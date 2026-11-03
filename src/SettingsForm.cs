using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OpaGachi
{
    // Settings (user, 2026-11-01/02): a small borderless glass window with tabs along the top (Pets, Agents,
    // Connections, Usage, Settings, Help) and a footer with the live summary and Done. Pets, Settings and Help
    // work like the iPhone Settings app: one grouped card of categories; a category opens its own page.
    // Every change applies to the running pets immediately and is saved straight away.
    sealed class SettingsForm : GlassForm
    {
        public const int PageCount = 6;
        public const int ConnectionsPage = 2;
        static readonly string[] PageNames = { "Pets", "Agents", "Connections", "Usage", "Settings", "Help" };
        static readonly string[] PageIcons = { "nav-pets", "nav-agents", "nav-connections", "nav-usage", "action-settings", "nav-help" };
        const int Inset = 16;   // row content starts 16 px inside its card

        readonly Settings settings;
        readonly PetManager pets;
        readonly TabBar tabs = new TabBar(PageNames, PageIcons) { Dock = DockStyle.Top, Size = new Size(720, 60) };
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        readonly FadeLabel status = new FadeLabel();
        readonly Control[] pages = new Control[PageCount];   // built the first time they're shown
        readonly Func<Control>[] builders;
        readonly Dictionary<string, Image> textureCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        readonly Timer refresh = new Timer { Interval = 1000 };
        readonly List<Action> refreshers = new List<Action>();   // live bits: statuses, usage, summary
        int current = -1;
        public Action<int, int> AppearanceChanged;   // the tray app reopens Settings on this tab and category in the new look
        public bool Restyled;   // reopened after a look change: no opening motion

        public SettingsForm(Settings settings, PetManager pets)
        {
            this.settings = settings;
            this.pets = pets;
            SuspendLayout();
            Text = "OpaGachi Settings";   // taskbar and Alt+Tab only: the window itself shows no title (user)
            ClientSize = new Size(720, 660);
            builders = new Func<Control>[] { BuildPetsPage, BuildAgentsPage, BuildConnectionsPage, BuildUsagePage, BuildSettingsPage, BuildHelpPage };

            var footer = BuildFooter();
            Controls.Add(footer);
            Controls.Add(tabs);
            Controls.Add(content);
            content.BringToFront();   // docks last: fills what the tabs and footer leave
            tabs.Selected = 0;
            tabs.Picked += i =>
            {
                var drill = i == current ? pages[i] as DrillPage : null;
                if (drill != null) drill.Open(-1, true);   // the open tab again: back to its list
                else ShowPage(i);
            };
            tabs.BarePress += (s, e) => StartDrag(e);
            AddCaptionButtons(tabs, true);

            ShowPage(0);
            refresh.Tick += (s, e) => { foreach (var r in refreshers.ToArray()) r(); };
            refresh.Start();
            foreach (var r in refreshers.ToArray()) r();
            ResumeLayout(true);
        }

        Control Page(int index)
        {
            if (pages[index] == null)
            {
                pages[index] = builders[index]();
                Theme.DarkScrollbars(pages[index]);
            }
            return pages[index];
        }

        // ---- Frame: footer, pages ----------------------------------------------------------------

        // Left: what's live right now; messages about changes fade in over it for a few seconds. Right: Done.
        Control BuildFooter()
        {
            var footer = new Panel { Dock = DockStyle.Bottom, Size = new Size(720, 76), BackColor = Color.Transparent };   // full width from the start, so right-anchored children stay put
            status.Location = new Point(24, 15);
            status.Size = new Size(460, 22);
            var done = new FlatButton("Done", 92) { Primary = true, Size = new Size(92, 30) };
            done.Location = new Point(720 - 24 - 92, 11);
            // The legal line at the very bottom (user, 2026-11-02).
            footer.Controls.Add(new Label { Text = Legal.Footer, Font = Theme.Ui(7.5F), ForeColor = Theme.TextMuted, BackColor = Color.Transparent, AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(0, 52, 720, 18), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top });
            done.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            done.Click += (s, e) => Close();
            AcceptButton = done;
            CancelButton = done;
            footer.Controls.Add(status);
            footer.Controls.Add(done);
            Drag(footer);
            refreshers.Add(() =>
            {
                var live = pets.Live.Where(p => !p.InstanceKey.EndsWith(":preview", StringComparison.Ordinal) && p.AgentKey != "demo").ToList();
                int working = live.Count(p => p.IsBusy), waiting = live.Count(p => p.Mode == Mode.Help);
                string a = live.Count == 1 ? "1 pet active" : live.Count + " pets active";
                string b = waiting > 0 ? waiting + " needs you" : working > 0 ? working + " working" : live.Count > 0 ? "all idle" : "nothing open";
                status.Default = a + " · " + b;
                status.Invalidate();
            });
            return footer;
        }

        // Window open (Gemini Response 3): the frame stays put; only the page content settles 12 px into place.
        Entrance opening;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (current == 3) { usage.UpdateFrom(pets); usage.Replay(Restyled ? 0 : Theme.StandardMs); }
            if (Restyled || Theme.ReduceMotion || current < 0 || content.Width <= 0 || content.Height <= 0) return;
            var shot = new Bitmap(content.Width, content.Height);
            content.DrawToBitmap(shot, new Rectangle(Point.Empty, content.Size));
            opening = new Entrance(shot).Add(new Rectangle(Point.Empty, content.Size), 0, 12);
            opening.Cover(content);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (opening != null && !opening.IsDisposed) opening.Play();
            opening = null;
        }

        public void ShowPage(int index)
        {
            if (index < 0 || index >= PageCount || index == current) return;
            if (opening != null && !opening.IsDisposed) { content.Controls.Remove(opening); opening.Dispose(); }
            opening = null;
            var page = Page(index);
            // Cover the old page with its own snapshot first, swap pages underneath, then slide the new one in:
            // no frame ever shows an empty page.
            PageTransition overlay = null;
            if (current >= 0 && Visible && !Theme.ReduceMotion && content.Width > 0 && content.Height > 0)
            {
                var before = new Bitmap(content.Width, content.Height);
                content.DrawToBitmap(before, new Rectangle(Point.Empty, content.Size));
                overlay = new PageTransition(before, index > current ? 1 : -1, false);
                content.Controls.Add(overlay);
                overlay.BringToFront();
                overlay.Refresh();
            }
            content.SuspendLayout();
            foreach (var p in pages) if (p != null && content.Controls.Contains(p)) content.Controls.Remove(p);
            content.Controls.Add(page);
            if (overlay != null) overlay.BringToFront();
            content.ResumeLayout(true);
            current = index;
            tabs.Selected = index;
            if (index == 3)   // Usage: the gauges sweep in once the page has arrived
            {
                usage.UpdateFrom(pets);
                usage.Replay(overlay != null ? 190 : 0);
            }
            if (overlay != null)
            {
                var after = new Bitmap(content.Width, content.Height);
                page.DrawToBitmap(after, new Rectangle(Point.Empty, content.Size));
                overlay.Start(after);
            }
        }

        // The open category on the current tab (-1: its list), so a look change can come back to it.
        public int CurrentCategory { get { var d = current >= 0 ? pages[current] as DrillPage : null; return d != null ? d.OpenIndex : -1; } }

        public void OpenCategory(int index)
        {
            var d = current >= 0 ? Page(current) as DrillPage : null;
            if (d != null && index >= 0) d.Open(index, false);
        }

        void ShowStatus(string text, bool isError) { status.Say(text, isError); }

        // A page: a scrolling column holding its card(s).
        static TableLayoutPanel NewPage(out Panel scroller)
        {
            scroller = new GlassScroller();
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, 16) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            scroller.Controls.Add(stack);
            return stack;
        }

        // A grouped card; the rows added after it go into it.
        static GlassCard NewCard(TableLayoutPanel stack)
        {
            var card = new GlassCard();
            stack.Controls.Add(card);
            stack.Tag = card;
            return card;
        }

        static void Put(TableLayoutPanel stack, Control c)
        {
            var card = stack.Tag as GlassCard;
            if (card != null) card.Controls.Add(c); else stack.Controls.Add(c);
        }

        // Label on the left, control on the right; every row is 44 px tall (taller controls get 6 px either side).
        const int RowHeight = 44;

        static TableLayoutPanel AddRow(TableLayoutPanel stack, string label, string hint, Control control)
        {
            int pad = Math.Max(6, (RowHeight - (control != null ? control.Height : 20)) / 2);
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(Inset, pad, Inset, pad), BackColor = Color.Transparent };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            // No hint line under the label (user: it looks cluttered); the explanation shows on hover instead.
            var text = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.TextPrimary, Font = Theme.Ui(10F), Margin = new Padding(0), BackColor = Color.Transparent };
            if (!string.IsNullOrEmpty(hint)) { Tip.Attach(text, () => hint); Tip.Attach(row, () => hint); }
            row.Controls.Add(text, 0, 0);
            if (control != null)
            {
                control.Anchor = AnchorStyles.Right;
                control.Margin = new Padding(12, 0, 0, 0);
                row.Controls.Add(control, 1, 0);
            }
            Put(stack, row);
            return row;
        }

        // One entry on a tab's category list.
        sealed class Category
        {
            public string Title, Icon;
            public Color Tint;
            public Func<string> Value;
            public Action<TableLayoutPanel> Build;
            public Category(string title, string icon, int tint, Func<string> value, Action<TableLayoutPanel> build)
            {
                Title = title; Icon = icon; Tint = Theme.Hex(tint); Value = value; Build = build;
            }
        }

        // A tab in iOS Settings style (user, 2026-11-02): one grouped card listing its categories; each opens a page
        // with a back capsule, the category's title and its rows in one card.
        DrillPage Categories(string tab, params Category[] items)
        {
            var drill = new DrillPage();
            Panel root;
            var stack = NewPage(out root);
            var card = NewCard(stack);
            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                var row = new CategoryRow(items[i].Title, items[i].Icon, items[i].Tint, items[i].Value);
                row.Click += (s, e) => drill.Open(index, true);
                card.Controls.Add(row);
            }
            drill.SetRoot(root, items.Select(c => (Func<Control>)(() =>
            {
                Panel page;
                var inner = NewPage(out page);
                inner.Controls.Add(new BackHeader(tab, c.Title, () => drill.Open(-1, true)));
                var group = NewCard(inner);
                group.Margin = new Padding(GlassCard.Side, 4, GlassCard.Side, 4);
                c.Build(inner);
                Theme.DarkScrollbars(page);
                return page;
            })).ToArray());
            return drill;
        }

        void Apply() { pets.ApplyAll(); }
        void Save() { settings.Save(); }

        public void SwitchTheme(bool dark)
        {
            if (dark == settings.Dark) return;
            settings.Dark = dark;
            Restyle();
        }

        // Rebuild the window in the new look (theme, contrast, text size) in the same place, on the same tab and
        // category.
        void Restyle()
        {
            Save();
            Theme.Apply(settings.Dark, settings.HighContrast, settings.TextScale);
            Apply();
            int tab = current, category = CurrentCategory;
            if (AppearanceChanged != null) BeginInvoke((Action)(() => AppearanceChanged(tab, category)));
        }

        // ---- Pets page --------------------------------------------------------------------------

        // Every control in the right-hand column is the same width, so they line up (user: unity, 2026-11-01).
        const int ControlWidth = 180;

        Control BuildPetsPage()
        {
            OpaGachi.Anchor[] anchors = { OpaGachi.Anchor.BottomLeft, OpaGachi.Anchor.BottomCenter, OpaGachi.Anchor.BottomRight, OpaGachi.Anchor.TopLeft, OpaGachi.Anchor.TopCenter, OpaGachi.Anchor.TopRight };
            Visibility[] modes = { Visibility.DesktopOnly, Visibility.AllButAiApps, Visibility.AllButOwnApp };
            string[] modeNames = { "On the desktop only", "Over all but AI apps", "Over all but their own app" };
            return Categories("Pets",
                new Category("Pet size", "cat-size", 0xFF9F0A, () => Settings.SizeNames[settings.Size], stack =>
                {
                    var size = new FlatSlider { Minimum = 0, Maximum = Settings.SizeNames.Length - 1, TickLabels = Settings.SizeNames, Size = new Size(ControlWidth + 40, 44) };
                    size.Value = settings.Size;
                    size.ValueChanged += (s, e) => { settings.Size = size.Value; Apply(); ShowStatus("Pet size: " + Settings.SizeNames[size.Value], false); };
                    size.Committed += (s, e) => Save();
                    AddRow(stack, "Size", "One size for every pet", size);
                }),
                new Category("Placement", "cat-pin", 0x30B0C7, () => settings.CustomPlacement ? "Custom spot" : Theme.Sentence(settings.Anchor.ToString()), stack =>
                {
                    var position = Dropdown(anchors.Select(x => Theme.Sentence(x.ToString())).ToArray(), () => Array.IndexOf(anchors, settings.Anchor),
                        i => { pets.SetAnchor(anchors[i]); ShowStatus("Position: " + Theme.Sentence(anchors[i].ToString()), false); }, ControlWidth);
                    AddRow(stack, "Position", "Where the row of pets starts (Settings › Experimental › Custom pet location puts them anywhere)", position);
                    // Displays (user, 2026-11-02): every connected screen with its size and orientation; pets use its
                    // work area, so a portrait screen or a side taskbar is handled too.
                    var screens = Screen.AllScreens;
                    var labels = new[] { "Main display" }.Concat(screens.Select((sc, n) => DisplayLabel(sc, n))).ToArray();
                    var display = Dropdown(labels, () => { int at = Array.FindIndex(screens, sc => sc.DeviceName == settings.Display); return at < 0 ? 0 : at + 1; },
                        i => { settings.Display = i == 0 ? "" : screens[i - 1].DeviceName; Apply(); Save(); ShowStatus("Pets show on " + (i == 0 ? "the main display" : labels[i]) + ".", false); }, ControlWidth + 60);
                    AddRow(stack, "Display", "Which screen the pets stand on; the list shows every connected display", display);
                    AddRow(stack, "Spacing", "Distance between pets", PercentDropdown(40, 120, () => settings.Spacing, v => settings.Spacing = v, "Spacing"));
                    var show = Dropdown(modeNames, () => Array.IndexOf(modes, settings.Visibility),
                        i => { settings.Visibility = modes[i]; Apply(); Save(); ShowStatus("Pets now show " + modeNames[i].ToLowerInvariant() + ".", false); }, ControlWidth);
                    AddRow(stack, "Show pets", "Desktop only: pets sit behind your windows.\nAll but AI apps: hidden while any AI app or agent terminal is in front.\nAll but their own app: each pet hides while its own AI is in front.", show);
                }),
                new Category("Behaviour", "cat-sparkle", 0xAF52DE, () => "Bored after " + (int)Math.Round(settings.BoredAfterSeconds / 60) + " min", stack =>
                {
                    var bored = new Stepper { Minimum = 1, Maximum = 120, Size = new Size(ControlWidth, 28) };
                    bored.Value = (int)Math.Round(settings.BoredAfterSeconds / 60);
                    bored.ValueChanged += (s, e) => { settings.BoredAfterSeconds = bored.Value * 60; Apply(); Save(); };
                    AddRow(stack, "Get bored after", "Idle time before the bored animation", bored);
                    AddRow(stack, "Fidget chance", "Chance of a little fidget every 8 seconds", PercentDropdown(0, 100, () => settings.OccasionalChance, v => settings.OccasionalChance = v, "Fidget chance"));
                    var badges = new ToggleSwitch { Checked = settings.Badges };
                    badges.CheckedChanged += (s, e) => { settings.Badges = badges.Checked; Apply(); Save(); };
                    AddRow(stack, "Status badges", "Show !, ✓, ✗, …, zz by a pet's head", badges);
                }),
                new Category("Interaction", "cat-cursor", 0x0A84FF, () => settings.ClickPets ? "Clickable" : "Click-through", stack =>
                {
                    var click = new ToggleSwitch { Checked = settings.ClickPets };
                    click.CheckedChanged += (s, e) =>
                    {
                        settings.ClickPets = click.Checked;
                        Apply();
                        Save();
                        ShowStatus(click.Checked ? "Click a pet to bring its app forward." : "Pets are click-through.", false);
                    };
                    AddRow(stack, "Click a pet to open its app", "Only the figure and its name catch clicks; right-click for more", click);
                    var hover = new ToggleSwitch { Checked = settings.HoverInfo };
                    hover.CheckedChanged += (s, e) => { settings.HoverInfo = hover.Checked; Save(); };
                    AddRow(stack, "Show status on hover", "The name tag says what the pet is doing", hover);
                }));
        }

        // Percentages in steps of 10 (user, 2026-11-01: dropdowns instead of sliders). A stored value off the
        // grid (e.g. 0.92 from an older version) shows as its nearest step until a step is picked.
        FlatButton PercentDropdown(int from, int to, Func<double> get, Action<double> set, string what)
        {
            var steps = Enumerable.Range(0, (to - from) / 10 + 1).Select(i => from + i * 10).ToArray();
            return Dropdown(steps.Select(v => v + "%").ToArray(),
                () => Math.Max(0, Math.Min(steps.Length - 1, (int)Math.Round(get() * 10) - from / 10)),
                i => { set(steps[i] / 100.0); Apply(); Save(); ShowStatus(what + ": " + steps[i] + "%", false); }, ControlWidth);
        }

        // A capsule showing the current choice; clicking it opens a menu of the options.
        FlatButton Dropdown(string[] labels, Func<int> selected, Action<int> pick, int width)
        {
            var button = new FlatButton(labels[Math.Max(0, selected())], width) { Chevron = true };
            button.Click += (s, e) =>
            {
                var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Theme.Ui(9.5F) };
                MenuFade.Attach(menu);
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    var item = new ToolStripMenuItem(labels[i]) { Checked = i == selected() };
                    item.Click += (s2, e2) => { pick(index); button.Text = labels[Math.Max(0, selected())]; };
                    menu.Items.Add(item);
                }
                menu.Closed += (s2, e2) => menu.BeginInvoke((Action)menu.Dispose);
                menu.Show(button, new Point(0, button.Height + 2));
            };
            return button;
        }

        // ---- Agents page ------------------------------------------------------------------------

        // Agents (user, 2026-10-31 / 11-01): one agent at a time, like a character select, all in one card. ‹ and ›
        // step through them; the agent's own pet plays its idle loop in its tie, with the name (in its name colour)
        // and page dots below. With custom sprites on there are no ties, so only the name colour stays.
        Control BuildAgentsPage()
        {
            Panel page;
            var stack = NewPage(out page);
            NewCard(stack);
            var list = pets.Agents.Values.Where(a => !a.Hidden).ToList();
            int index = 0;
            AgentInfo info = null;
            bool loading = false;
            var stage = new Panel { Dock = DockStyle.Fill, Height = 262, Margin = new Padding(0), BackColor = Color.Transparent };
            var pet = new PetPreview(pets.Library) { Size = new Size(186, 202) };
            var name = new NameLabel { Size = new Size(300, 26) };
            var prev = new ChevronButton(-1);
            var next = new ChevronButton(1);
            var toggle = new ToggleSwitch();
            FlatButton trigger = null, sparkle = null;
            var pattern = new FlatButton("", ControlWidth) { Chevron = true };
            var colour = new FlatButton("", 28);
            var nameColour = new FlatButton("", ControlWidth) { Chevron = true };
            var pager = new Panel { Size = new Size(200, 18), BackColor = Color.Transparent };
            pager.Paint += (s, e) =>   // drawn, not ● ○ glyphs: fonts draw ● smaller than ○
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float x = (pager.Width - (list.Count * 14 - 8)) / 2f;
                for (int i = 0; i < list.Count; i++, x += 14)
                {
                    var dot = new RectangleF(x, 6, 6, 6);
                    if (i == index) using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillEllipse(b, dot);
                    else using (var p = new Pen(Theme.TextMuted)) e.Graphics.DrawEllipse(p, dot);
                }
            };
            stage.Controls.AddRange(new Control[] { prev, pet, next, name, pager });
            page.ParentChanged += (s, e) => pet.Play();   // the idle loop pauses while another page is showing

            Action layout = () =>
            {
                int cx = stage.Width / 2;
                pet.Location = new Point(cx - 93, 6);
                prev.Location = new Point(cx - 93 - 24 - 36, 6 + 101 - 24);   // chevrons level with the pet's middle
                next.Location = new Point(cx + 93 + 24, 6 + 101 - 24);
                name.Location = new Point(cx - 150, 210);
                pager.Location = new Point(cx - 100, 238);
            };
            stage.Resize += (s, e) => layout();

            Action<int> show = dir =>
            {
                info = list[index];
                loading = true;
                pet.Dimmed = !info.Enabled;
                pet.SetAgent(info, dir);
                name.Text = info.Name;
                name.Style = NameFill.Of(info, info.Key);
                toggle.Checked = info.Enabled;
                pattern.Text = TieLabel(info);
                if (trigger != null) { trigger.Text = info.SparkleTrigger; trigger.Enabled = info.Enabled; }
                if (sparkle != null) { sparkle.Text = info.SparkleColor ?? "Follow name colour"; sparkle.Enabled = info.Enabled; }
                colour.Swatch = info.Tie.HasValue ? info.Tie.Value : Theme.Elevated;
                nameColour.Text = NameFill.Label(name.Style);
                pattern.Enabled = colour.Enabled = nameColour.Enabled = info.Enabled;
                pager.Invalidate();
                loading = false;
            };
            Action commit = () => { pets.SaveAgents(); Apply(); show(0); };

            prev.Click += (s, e) => { index = (index - 1 + list.Count) % list.Count; show(-1); };
            next.Click += (s, e) => { index = (index + 1) % list.Count; show(1); };
            toggle.CheckedChanged += (s, e) =>
            {
                if (loading || info == null) return;
                info.Enabled = toggle.Checked;
                commit();
                ShowStatus(info.Name + (info.Enabled ? " pet is on." : " pet is off."), false);
            };
            pattern.Click += (s, e) => TieMenu(info, commit).Show(pattern, new Point(0, pattern.Height + 2));
            colour.Click += (s, e) => PickColour(info.Tie ?? Theme.Accent, c => { info.Tie = c; commit(); ShowStatus(info.Name + " tie colour updated.", false); });
            nameColour.Click += (s, e) => NameMenu(info, commit).Show(nameColour, new Point(0, nameColour.Height + 2));
            if (list.Count > 0) show(0);
            Put(stack, stage);
            AddRow(stack, "Show pet", "Show a pet while this agent is open", toggle);
            trigger = Dropdown(new[] { "On Hover", "On Click", "Both" }, () => Array.IndexOf(new[] { "On Hover", "On Click", "Both" }, info.SparkleTrigger), i => { info.SparkleTrigger = new[] { "On Hover", "On Click", "Both" }[i]; commit(); }, ControlWidth);
            sparkle = new FlatButton(info.SparkleColor ?? "Follow name colour", ControlWidth) { Chevron = true };
            sparkle.Click += (s,e) => SparkleMenu(info, commit).Show(sparkle, new Point(0, sparkle.Height + 2));
            AddRow(stack, "Sparkle trigger", "Choose hover, click or both", trigger);
            AddRow(stack, "Sparkle colour", "Gemini sparkles in a standard, gradient or metallic colour", sparkle);
            AddRow(stack, "Name colour", "A gradient preset or a colour of your own for the name above this agent's pets", nameColour);
            if (!settings.CustomSprites)   // custom sprites have no tie (user, 2026-11-01)
            {
                AddRow(stack, "Tie pattern", "Solid colour, a preset pattern or your own image", pattern);
                AddRow(stack, "Tie colour", "Shows through a pattern's clear parts", colour);
            }

            var reset = new FlatButton("Reset to defaults", 150);
            reset.Click += (s, e) =>
            {
                foreach (var d in DefaultTies)
                {
                    AgentInfo a;
                    if (!pets.Agents.TryGetValue(d[0], out a)) continue;
                    a.NameColor = null;
                    if (settings.CustomSprites) continue;
                    a.Tie = ColorTranslator.FromHtml(d[1]);
                    a.Texture = d[2];
                }
                commit();
                ShowStatus(settings.CustomSprites ? "Every name colour is back to its default." : "Every tie and name is back to its default.", false);
            };
            AddRow(stack, settings.CustomSprites ? "Name colours" : "Ties and name colours", "Back to how they shipped", reset);
            return page;
        }

        void PickColour(Color start, Action<Color> set)
        {
            using (var dlg = new ColorDialog { FullOpen = true, Color = start })
                if (dlg.ShowDialog(this) == DialogResult.OK) set(dlg.Color);
        }

        // Name colours (user, 2026-11-02): the four gradient presets, white, or a colour of your own.
        ContextMenuStrip SparkleMenu(AgentInfo info, Action commit)
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
            var follow = new ToolStripMenuItem("Follow name colour") { Checked = info.SparkleColor == null };
            follow.Click += (s,e) => { info.SparkleColor = null; commit(); }; menu.Items.Add(follow);
            foreach (var group in new[] { "Basic Sparkles", "Special / Gradient & Metallic Sparkles" })
            {
                var cat = new ToolStripMenuItem(group); menu.Items.Add(cat);
                foreach (var name in SparklePalette.Names.Where((n,i) => (i < 7) == (group == "Basic Sparkles")))
                {
                    string chosen = name;
                    var item = new ToolStripMenuItem(name) { Checked = info.SparkleColor == name };
                    item.Click += (s,e) => { info.SparkleColor = chosen; commit(); }; cat.DropDownItems.Add(item);
                }
            }
            menu.Closed += (s,e) => menu.BeginInvoke((Action)menu.Dispose);
            return menu;
        }

        ContextMenuStrip NameMenu(AgentInfo info, Action commit)
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = true, ShowCheckMargin = true, Font = Theme.Ui(9.5F) };
            MenuFade.Attach(menu);
            string currentStyle = NameFill.Of(info, info.Key);
            foreach (var style in NameFill.Presets.Concat(new[] { NameFill.White }))
            {
                string chosen = style;
                var item = new ToolStripMenuItem(NameFill.Label(style) + (style == NameFill.DefaultFor(info.Key) ? " (default)" : "")) { Image = NameSwatch(style), Checked = style == currentStyle };
                item.Click += (s, e) => { info.NameColor = chosen == NameFill.DefaultFor(info.Key) ? null : chosen; commit(); ShowStatus(info.Name + "'s name: " + NameFill.Label(chosen) + ".", false); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var custom = new ToolStripMenuItem("Custom colour...") { Checked = NameFill.Label(currentStyle) == "Custom colour" };
            custom.Click += (s, e) => PickColour(NameFill.Stops(currentStyle)[0], c => { info.NameColor = Settings.HexOf(c); commit(); ShowStatus(info.Name + "'s name colour updated.", false); });
            menu.Items.Add(custom);
            menu.Closed += (s, e) => menu.BeginInvoke((Action)menu.Dispose);
            return menu;
        }

        static Image NameSwatch(string style)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(1.5f, 1.5f, 13, 13);
                using (var b = NameFill.Brush(style, r)) g.FillEllipse(b, r);
                using (var p = new Pen(Color.FromArgb(160, 0, 0, 0))) g.DrawEllipse(p, r);
            }
            return bmp;
        }

        // The ties as shipped (the user's picks, 2026-10-30/31): agent, colour, pattern.
        static readonly string[][] DefaultTies =
        {
            new[] { "claude", "#D97757", "assets/ties/presets/CLAUDE1/design-1-60x150.png" },
            new[] { "chatgpt", "#5B2BA0", "assets/ties/presets/GPT1/design-1-60x150.png" },
            new[] { "gemini", "#F3D9E5", "assets/ties/presets/GEMINI/design-1-60x150.png" },
            new[] { "grok", "#111114", "assets/ties/presets/GROK/design-1-60x150.png" },
        };

        ContextMenuStrip TieMenu(AgentInfo info, Action commit)
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = true, ShowCheckMargin = true, Font = Theme.Ui(9F) };
            MenuFade.Attach(menu);
            var solid = new ToolStripMenuItem("Solid colour") { Checked = string.IsNullOrEmpty(info.Texture) };
            solid.Click += (s, e) => { info.Texture = null; commit(); ShowStatus(info.Name + " now has a solid tie.", false); };
            menu.Items.Add(solid);
            menu.Items.Add(new ToolStripSeparator());
            string presetDir = Path.Combine(Program.Root, Path.Combine("assets", Path.Combine("ties", "presets")));
            if (Directory.Exists(presetDir))
            {
                foreach (var group in Directory.GetDirectories(presetDir).OrderBy(f => f))
                {
                    var category = new ToolStripMenuItem(Path.GetFileName(group));
                    menu.Items.Add(category);
                    foreach (var file in Directory.GetFiles(group, "*.png").OrderBy(f => f))
                {
                    string rel = "assets/ties/presets/" + Path.GetFileName(group) + "/" + Path.GetFileName(file);
                    var item = new ToolStripMenuItem(PresetLabel(file)) { Image = Thumbnail(rel), Checked = string.Equals(rel, info.Texture, StringComparison.OrdinalIgnoreCase) };
                    string chosen = rel;
                    item.Click += (s, e) => { info.Texture = chosen; commit(); ShowStatus(info.Name + " tie: " + PresetLabel(chosen) + ".", false); };
                    category.DropDownItems.Add(item);
                    }
                }
            }
            menu.Items.Add(new ToolStripSeparator());
            var custom = new ToolStripMenuItem("Custom image...");
            custom.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Title = "Choose a tie pattern", Filter = "Images (*.png;*.jpg)|*.png;*.jpg" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        // absolute path into the user's data folder; Path.Combine(Program.Root, rel) keeps it as is
                        string rel = Path.Combine(Path.Combine(Program.Data, "ties"), info.Key + "-custom.png");
                        Directory.CreateDirectory(Path.GetDirectoryName(rel));
                        using (var img = Image.FromFile(dlg.FileName)) img.Save(rel, ImageFormat.Png);
                        textureCache.Remove(rel);
                        info.Texture = rel;
                        commit();
                        ShowStatus(info.Name + " tie: custom image.", false);
                    }
                    catch (Exception ex) { ShowStatus("Couldn't use that image: " + ex.Message, true); }
                }
            };
            menu.Items.Add(custom);
            menu.Closed += (s, e) => menu.BeginInvoke((Action)menu.Dispose);
            return menu;
        }

        static string PresetLabel(string file)
        {
            string parent = Path.GetFileName(Path.GetDirectoryName(file));
            string filename = Path.GetFileNameWithoutExtension(file);
            if (filename.StartsWith("design-")) return parent + " · Design " + filename.Split('-')[1];
            string stem = Path.GetFileNameWithoutExtension(file);
            int dash = stem.IndexOf('-');
            string rest = (dash >= 0 ? stem.Substring(dash + 1) : stem).Replace('-', ' ');
            return rest.Length == 0 ? stem : char.ToUpperInvariant(rest[0]) + rest.Substring(1);
        }

        static string TieLabel(AgentInfo info)
        {
            if (string.IsNullOrEmpty(info.Texture)) return "Solid colour";
            return info.Texture.Replace('\\', '/').IndexOf("/presets/", StringComparison.OrdinalIgnoreCase) >= 0 ? PresetLabel(info.Texture) : "Custom image";
        }

        Image LoadTexture(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return null;
            Image img;
            if (textureCache.TryGetValue(rel, out img)) return img;
            try
            {
                string full = Path.Combine(Program.Root, rel.Replace('/', Path.DirectorySeparatorChar));
                using (var fromDisk = Image.FromFile(full)) img = new Bitmap(fromDisk);   // copy so the file isn't locked
            }
            catch (Exception) { img = null; }
            textureCache[rel] = img;
            return img;
        }

        Image Thumbnail(string rel)
        {
            var src = LoadTexture(rel);
            if (src == null) return null;
            var thumb = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, new Rectangle(0, 0, 16, 16));
                using (var p = new Pen(Theme.BorderStrong)) g.DrawRectangle(p, 0, 0, 15, 15);
            }
            return thumb;
        }

        // ---- Connections page -------------------------------------------------------------------

        Control BuildConnectionsPage()
        {
            Panel page;
            var stack = NewPage(out page);
            var card = NewCard(stack);
            var rows = new Dictionary<string, ConnectionRow>();
            Action sync = () =>
            {
                var items = Integrations.Connections(pets);
                if (items.Count != rows.Count || items.Any(c => !rows.ContainsKey(c.Id)))
                {
                    card.SuspendLayout();
                    card.Controls.Clear();
                    rows.Clear();
                    foreach (var c in items)
                    {
                        var r = new ConnectionRow();
                        r.Action.Click += (s, e) => RunConnectionAction(r);
                        rows[c.Id] = r;
                        card.Controls.Add(r);
                    }
                    card.ResumeLayout(true);
                }
                foreach (var c in items)
                {
                    AgentInfo info;
                    pets.Agents.TryGetValue(c.Agent, out info);
                    rows[c.Id].Set(c, info != null && info.Tie.HasValue ? info.Tie.Value : Theme.TextMuted);
                }
            };
            refreshers.Add(sync);
            sync();
            return page;
        }

        void RunConnectionAction(ConnectionRow row)
        {
            var c = row.Item;
            if (c == null || !c.ActionEnabled) return;
            hookChanged = false;
            try
            {
                RunHookAction(c);
            }
            catch (Exception ex) { ShowStatus("Couldn't update " + c.Title + ": " + ex.Message, true); }
            foreach (var r in refreshers) r();
            if (hookChanged) Program.AskRestart(this, "--settings --page " + ConnectionsPage);   // comes back on this page
        }

        bool hookChanged;

        void RunHookAction(Connection c)
        {
            switch (c.Id)
            {
                case "claude-code":
                    ToggleFile("Claude Code hooks", ClaudeCode.DefaultSettingsPath(), Integrations.ClaudeHooks,
                        "Every Claude Code session will post its activity, including approval prompts, to OpaGachi. If OpaGachi isn't running, Claude Code carries on normally.",
                        () => ClaudeCode.InstallHooks(ClaudeCode.DefaultSettingsPath(), settings.Port, Application.ExecutablePath),
                        () => ClaudeCode.UninstallHooks(ClaudeCode.DefaultSettingsPath()));
                    break;
                case "relay":
                    ToggleFile("status line relay", ClaudeCode.DefaultSettingsPath(), Integrations.Relay,
                        "Claude Code will run OpaGachi in front of your status line command; your status line shows exactly as before, and OpaGachi reads the plan limits.",
                        () => StatusLineRelay.Install(ClaudeCode.DefaultSettingsPath(), Application.ExecutablePath),
                        () => StatusLineRelay.Uninstall(ClaudeCode.DefaultSettingsPath()));
                    break;
                case "codex":
                    ToggleFile("Codex approval hook", CodexHooks.DefaultPath(), Integrations.CodexHook,
                        "It only tells the pet to raise its hand; Codex still asks you as usual. Codex runs it after you trust it with /hooks.",
                        () => CodexHooks.Install(CodexHooks.DefaultPath(), Application.ExecutablePath),
                        () => CodexHooks.Uninstall(CodexHooks.DefaultPath()));
                    break;
                case "gemini-cli":
                    ToggleFile("Gemini CLI hooks", GeminiHooks.DefaultPath(), Integrations.GeminiHook,
                        "Gemini CLI will report prompts, tool use, permission requests and finished turns to OpaGachi. The hook makes no decisions.",
                        () => GeminiHooks.Install(GeminiHooks.DefaultPath(), Application.ExecutablePath),
                        () => GeminiHooks.Uninstall(GeminiHooks.DefaultPath()));
                    break;
                case "grok":
                    ToggleFile("Grok Build hook", GrokHooks.FilePath, GrokHooks.Installed,
                        "Adds its own file in ~/.grok/hooks; nothing else is changed. Grok Build posts its activity to OpaGachi.",
                        () => GrokHooks.Install(settings.Port), GrokHooks.Uninstall);
                    break;
            }
        }

        // Install/remove behind the confirmation dialog (the one popup the design rules allow).
        void ToggleFile(string what, string path, bool installed, string explain, Action install, Action remove)
        {
            if (installed)
            {
                remove();
                hookChanged = true;
                ShowStatus(Capital(what) + " removed.", false);
                return;
            }
            var answer = MessageBox.Show(this, "Add the " + what.ToLowerInvariant() + "?\n\n" + path + "\n\n" + explain + "\n\nA backup of the file is saved first where one exists.",
                "OpaGachi", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;
            install();
            hookChanged = true;
            ShowStatus(Capital(what) + " added.", false);
        }

        static string Capital(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        void SetStartup(bool on, bool windowed)
        {
            try
            {
                Startup.Set(on, windowed);
                ShowStatus(!on ? "OpaGachi won't start with Windows." : windowed ? "OpaGachi opens this window when you sign in." : "OpaGachi starts in the tray when you sign in.", false);
            }
            catch (Exception ex) { ShowStatus("Couldn't change startup: " + ex.Message, true); }
        }

        // ---- Usage page -------------------------------------------------------------------------

        UsagePanel usage;

        Control BuildUsagePage()
        {
            var scroller = new GlassScroller();
            usage = new UsagePanel { Dock = DockStyle.Top };
            scroller.Controls.Add(usage);
            refreshers.Add(() => { if (current == 3) usage.UpdateFrom(pets); });
            return scroller;
        }

        // ---- Settings page ----------------------------------------------------------------------

        // Look, accessibility, sound and experiments (user, 2026-11-01), plus the legal pages and credits that
        // Help also lists.
        Control BuildSettingsPage()
        {
            return Categories("Settings",
                new Category("Appearance", "cat-theme", 0x5856D6, () => settings.Dark ? "Dark" : "Light", stack =>
                {
                    var look = Dropdown(new[] { "Light", "Dark" }, () => settings.Dark ? 1 : 0, i => SwitchTheme(i == 1), ControlWidth);
                    AddRow(stack, "Theme", "Light or dark glass; both keep the cherry-red accent", look);
                }),
                new Category("Accessibility", "cat-access", 0x0A84FF, () => settings.HighContrast ? "High contrast" : Settings.TextSizeNames[settings.TextSize] + " text", stack =>
                {
                    var motion = new ToggleSwitch { Checked = settings.ReduceMotion };
                    motion.CheckedChanged += (s, e) =>
                    {
                        settings.ReduceMotionChoice = motion.Checked;
                        Theme.ReduceMotion = motion.Checked;
                        Save();
                        ShowStatus(motion.Checked ? "Reduced motion: fades only." : "Full motion is back.", false);
                    };
                    AddRow(stack, "Reduce motion", "Fades only: no slides, hops or sparkles", motion);
                    var text = Dropdown(Settings.TextSizeNames, () => settings.TextSize, i =>
                    {
                        if (i == settings.TextSize) return;
                        settings.TextSize = i;
                        Restyle();
                    }, ControlWidth);
                    AddRow(stack, "Text size", "Bigger or smaller text in these windows", text);
                    var contrast = new ToggleSwitch { Checked = settings.HighContrast };
                    contrast.CheckedChanged += (s, e) => { settings.HighContrast = contrast.Checked; Restyle(); };
                    AddRow(stack, "High contrast", "Solid surfaces, strong text and edges instead of glass", contrast);
                }),
                new Category("Sound", "cat-sound", 0xFF375F, () => settings.Sound ? Sound.Label(settings.SoundFile) : "Off", stack =>
                {
                    var sound = new ToggleSwitch { Checked = settings.Sound };
                    var pick = new FlatButton(Sound.Label(settings.SoundFile), ControlWidth) { Chevron = true, Enabled = settings.Sound };
                    sound.CheckedChanged += (s, e) =>
                    {
                        settings.Sound = sound.Checked;
                        pick.Enabled = sound.Checked;
                        Save();
                        if (sound.Checked) Sound.Play(settings.SoundFile);
                        ShowStatus(sound.Checked ? "A sound plays when an agent finishes." : "Completion sound is off.", false);
                    };
                    pick.Click += (s, e) => SoundMenu(pick).Show(pick, new Point(0, pick.Height + 2));
                    AddRow(stack, "Completion sound", "Play a sound when an agent finishes its work", sound);
                    AddRow(stack, "Sound", "Pick one to hear it, or add your own MP3 or WAV", pick);
                }),
                new Category("Startup", "cat-power", 0xFF9F0A, () => !Startup.Enabled ? "Off" : Startup.Windowed ? "Settings window" : "Minimized", stack =>
                {
                    var run = new ToggleSwitch { Checked = Startup.Enabled };
                    var mode = Dropdown(new[] { "Minimized to tray", "Settings window" }, () => Startup.Windowed ? 1 : 0, i => SetStartup(true, i == 1), ControlWidth);
                    mode.Enabled = run.Checked;
                    run.CheckedChanged += (s, e) => { mode.Enabled = run.Checked; SetStartup(run.Checked, Startup.Windowed); };
                    AddRow(stack, "Run at startup", "Start OpaGachi when you sign in to Windows", run);
                    AddRow(stack, "Start as", "Quietly in the tray, or with this window open", mode);
                }),
                new Category("Experimental", "cat-flask", 0x34C759, () => settings.CustomSprites ? "Custom sprites" : "Off", stack =>
                {
                    var custom = new ToggleSwitch { Checked = settings.CustomSprites };
                    var actionCards = new List<Control>();
                    var file = new FlatButton("Animation slots...", 154) { Visible = settings.CustomSprites };
                    bool reverting = false;
                    custom.CheckedChanged += (s, e) =>
                    {
                        if (reverting) return;
                        if (custom.Checked && MessageBox.Show(this, "Custom sprites replace each agent's character and hide ties. Add up to five animations per action. Use only PNGs or only alpha WebMs across all actions. Name files ACTION_1 through ACTION_5, as shown on each card. Missing actions use your Idle clip, then the built-in animation.",
                                "Custom sprites", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                        {
                            reverting = true;
                            custom.Checked = false;
                            reverting = false;
                            return;
                        }
                        settings.CustomSprites = custom.Checked;
                        file.Visible = custom.Checked;
                        foreach (var card in actionCards) card.Visible = custom.Checked;
                        Save();
                        pets.RefreshSprites();
                        ShowStatus(custom.Checked ? (pets.Library.HasCustom ? "Every pet uses your sprite." : "Every pet uses the generic sprite. Choose a file to use your own.") : "The pets are back in their ties.", false);
                        if (pages[1] != null) { pages[1].Dispose(); pages[1] = null; }   // the Agents page loses or regains its tie rows
                    };
                    file.Click += (s, e) => { using (var slots = new SpriteSlotsForm(pets)) slots.ShowDialog(this); };
                    AddRow(stack, "Custom sprites", "One sprite of your own for every agent, instead of the girl in her tie", custom);
                    AddRow(stack, "Manage all animations", "Add or clear custom action slots", file);
                    foreach (var spec in AnimSpec.All)
                    {
                        var action = spec.Anim;
                        var card = NewCard(stack); card.Visible = settings.CustomSprites; actionCards.Add(card);
                        var add = new FlatButton("Animation slots...", 154);
                        add.Click += (s,e) => { using (var slots = new SpriteSlotsForm(pets, action)) slots.ShowDialog(this); };
                        AddRow(stack, Theme.Sentence(action.ToString()), spec.Folder.ToUpperInvariant() + "_1 through _5. Five slots. PNGs OR alpha WebMs globally.", add);
                    }
                    NewCard(stack);

                    // Custom pet location (user, 2026-11-02): drag the row anywhere on the chosen display.
                    var place = new ToggleSwitch { Checked = settings.CustomPlacement };
                    var move = new FlatButton("Move pets...", 132) { Enabled = settings.CustomPlacement };
                    bool revertingPlace = false;
                    place.CheckedChanged += (s, e) =>
                    {
                        if (revertingPlace) return;
                        if (place.Checked && !EditPlacement())
                        {
                            revertingPlace = true;
                            place.Checked = false;
                            revertingPlace = false;
                            return;
                        }
                        settings.CustomPlacement = place.Checked;
                        move.Enabled = place.Checked;
                        Apply();
                        Save();
                        ShowStatus(place.Checked ? "The pets stand where you put them." : "The pets follow Position again.", false);
                    };
                    move.Click += (s, e) => { if (EditPlacement()) { Apply(); Save(); ShowStatus("The pets moved.", false); } };
                    AddRow(stack, "Custom pet location", "Show the desktop and drag the pets to any spot", place);
                    AddRow(stack, "Pet location", "Drag them again", move);
                }),
                new Category("Factory reset", "action-settings", 0xFF453A, null, stack =>
                {
                    var reset = new FlatButton("Factory Reset", 160);
                    Put(stack, new NoteItem("Start fresh", "Clears preferences, custom sprites, uploaded ties and sounds, and OpaGachi hooks. Other apps' settings and unrelated hooks are preserved. The app restarts with its original defaults.", null));
                    reset.Click += (s,e) =>
                    {
                        using (var confirm = new ConfirmForm("Reset all OpaGachi settings, uploads and hooks? This cannot be undone from the app.", "Factory Reset"))
                            if (confirm.ShowDialog(this) != DialogResult.OK) return;
                        try { FactoryReset.Run(Program.Root, Program.Data); Startup.Set(false, false); Program.Restart(""); }
                        catch (Exception ex) { MessageBox.Show(this, "Reset did not finish: " + ex.Message, "Factory reset", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                    };
                    AddRow(stack, "Factory Reset", "Return to the original defaults", reset);
                }),
                LegalCategory(),
                CreditsCategory());
        }

        // "Display 1 · 1080 × 1920 · portrait": Windows' own number, the size and the orientation.
        static string DisplayLabel(Screen screen, int index)
        {
            string digits = new string(screen.DeviceName.Where(char.IsDigit).ToArray());
            var b = screen.Bounds;
            return "Display " + (digits.Length > 0 ? digits : (index + 1).ToString()) + " · " + b.Width + " × " + b.Height + " · " + (b.Height > b.Width ? "portrait" : "landscape") + (screen.Primary ? " · main" : "");
        }

        // The custom location editor on the pets' display; true when the user saved a spot.
        bool EditPlacement()
        {
            int step, count;
            var preview = pets.RowPreview(out step, out count);
            using (var editor = new PlacementForm(pets.TargetScreen(), preview, step, count, settings.CustomX, settings.CustomY))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return false;
                settings.CustomX = editor.X;
                settings.CustomY = editor.Y;
                return true;
            }
        }

        // Privacy policy, terms, what's collected, and credits (user, 2026-11-01: in Help and in Settings).
        static Category LegalCategory()
        {
            return new Category("Privacy and legal", "cat-lock", 0x8E8E93, null, stack => { foreach (var q in Legal.Items) Put(stack, new FaqItem(q[0], q[1])); });
        }

        static Category CreditsCategory()
        {
            return new Category("Credits", "cat-star", 0xFF9500, () => "Slushie4LiFe", stack => { foreach (var q in Legal.Credits) Put(stack, new NoteItem(q[0], q[1], q.Length > 2 ? q[2] : null)); });
        }

        ContextMenuStrip SoundMenu(FlatButton pick)
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Theme.Ui(9.5F) };
            MenuFade.Attach(menu);
            foreach (var name in Sound.All())   // alphabetical (user)
            {
                string chosen = name;
                var item = new ToolStripMenuItem(Sound.Label(name)) { Checked = string.Equals(name, settings.SoundFile, StringComparison.OrdinalIgnoreCase) };
                item.Click += (s, e) => { settings.SoundFile = chosen; Save(); pick.Text = Sound.Label(chosen); Sound.Play(chosen); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var upload = new ToolStripMenuItem("Upload your own sound...");
            upload.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Title = "Choose a sound", Filter = "Sounds (*.mp3;*.wav)|*.mp3;*.wav" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        settings.SoundFile = Sound.Add(dlg.FileName);
                        Save();
                        pick.Text = Sound.Label(settings.SoundFile);
                        Sound.Play(settings.SoundFile);
                        ShowStatus("Your sound \"" + pick.Text + "\" is set.", false);
                    }
                    catch (Exception ex) { ShowStatus("Couldn't use that sound: " + ex.Message, true); }
                }
            };
            menu.Items.Add(upload);
            menu.Closed += (s, e) => menu.BeginInvoke((Action)menu.Dispose);
            return menu;
        }

        void ImportSprite(FlatButton button)
        {
            using (var dlg = new OpenFileDialog { Title = "Choose a sprite", Filter = "Sprites (*.png;*.webm)|*.png;*.webm" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string fileName = dlg.FileName;
                button.Enabled = false;
                ShowStatus(fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? "Reading the video..." : "Reading the image...", false);
                var lib = pets.Library;
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>   // a video takes a few seconds; keep the window responsive
                {
                    string error = null;
                    int frames = 0;
                    try { frames = CustomSprite.Import(fileName, lib.Root, lib.Width, lib.Height); }
                    catch (Exception ex) { error = ex.Message; Log.Write("custom sprite: " + ex); }
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            button.Enabled = settings.CustomSprites;
                            if (error != null) { ShowStatus("Couldn't use that file: " + error, true); return; }
                            button.Text = "Replace file...";
                            pets.RefreshSprites();
                            ShowStatus(frames > 1 && fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? "Your sprite is in: " + frames + " frames." : "Your sprite is in.", false);
                        }));
                    }
                    catch (InvalidOperationException) { }   // the window closed meanwhile
                });
            }
        }

        // ---- Help page --------------------------------------------------------------------------

        // Help: the questions people ask when something doesn't show up (user, 2026-10-31), the legal pages, credits
        // and the log, as categories like Settings.
        Control BuildHelpPage()
        {
            string[][] faq =
            {
                new[] { "Which apps get a pet?", "Desktop apps and command-line agents: the Claude, ChatGPT, Gemini and Grok desktop apps, Claude Code, Codex, Gemini CLI and Grok Build. Websites, including ones installed as apps from a browser, don't get pets." },
                new[] { "How do I put the pets somewhere else, or on another screen?", "Pets › Placement: pick a Position, and a Display (every connected screen is listed with its size and whether it's portrait or landscape). For an exact spot, turn on Settings › Experimental › Custom pet location: the desktop shows with your pets on a dotted line; drag it where you want them and choose Save. Move pets... drags them again." },
                new[] { "Can I change a pet's name colour?", "Agents › Name colour. Each agent has its own gradient by default (Claude orange, ChatGPT astral purple, Gemini pearl, Grok black to silver); pick another preset, white or any colour." },
                new[] { "How do I get a sound when an agent finishes?", "Settings › Sound: turn on Completion sound, then pick a sound (you hear it as you pick) or upload your own MP3 or WAV." },
                new[] { "Custom sprites: naming, slots and formats", "Settings › Experimental › Custom sprites › Animation slots. Each action card accepts up to five files: uppercase action plus _1 through _5, for example IDLE_1.png, IMAGEGEN_2.webm or WALK_1.png. Cards show every exact action name. Use only PNGs OR only WebMs with alpha across ALL actions, never both. The first import locks the format until Clear all custom sprites. Importing the same numbered slot replaces it. Missing actions use custom Idle, then built-in clips. Ties are hidden; name colours remain. WebMs are limited to 10 seconds and 40 MB." },
                new[] { "The text is small or hard to read.", "Settings › Accessibility: a bigger Text size, High contrast for solid surfaces and strong text, and Reduce motion for fades only." },
                new[] { "Why don't Gemini and Grok show plan limits?", "Neither writes its limits anywhere other apps can read. Grok keeps them in its own /usage screen; the Usage page still shows Grok's tokens and estimated cost from its session records. The Gemini app keeps no usage at all on your PC, so OpaGachi counts the answers it sees instead." },
                new[] { "Why don't Claude's 5-hour and weekly limits show?", "Claude Code only shares them with its status line. Add the Claude plan limits relay on the Connections page and they appear after its next reply; your status line stays as it is." },
                new[] { "An app is open but its pet doesn't appear.", "Desktop apps are watched automatically while they're open, even minimised. Make sure the agent is switched on under Agents." },
                new[] { "What do the hooks on the Connections page add?", "Instant approval alerts: the pet raises its hand the moment an agent waits for you. Everything else works without them." },
                new[] { "Why does OpaGachi ask to restart after I change hooks?", "So every pet starts fresh with the new connection. Choose Later to keep working, then pick Restart in the tray icon's menu whenever you're ready." },
                new[] { "How do I start OpaGachi with Windows?", "Settings › Startup: turn on Run at startup, then choose Minimized to tray or Settings window." },
                new[] { "How do I update or uninstall OpaGachi?", "To update, run a newer OpaGachi Setup over your install: it keeps your settings, custom sprites, ties and sounds. To remove it, use Windows Settings › Apps › Installed apps › OpaGachi › Uninstall. It takes its hooks out of your agents first and asks whether to delete your data." },
                new[] { "The pets cover something I need to click.", "Under Pets › Placement, set Show pets to On the desktop only or move them (Custom pet location), or switch off Pets › Interaction › Click a pet to open its app." },
            };
            return Categories("Help",
                new Category("Questions", "nav-help", 0x0A84FF, () => faq.Length.ToString(), stack => { foreach (var q in faq) Put(stack, new FaqItem(q[0], q[1])); }),
                new Category("Custom sprites rules", "cat-flask", 0x34C759, () => "5 per action", stack =>
                {
                    Put(stack, new NoteItem("Choose one format for every action", "Use only PNGs OR only WebMs with a transparent alpha background across all custom actions. The first successful import locks the format. Clear all custom sprites to unlock it.", null));
                    Put(stack, new NoteItem("Five numbered slots", "Each action accepts slots 1 through 5. Importing the same numbered name replaces that slot. WebMs must be at most 10 seconds and 40 MB. Missing actions use custom Idle, then built-in clips.", null));
                    foreach (var spec in AnimSpec.All) Put(stack, new NoteItem(Theme.Sentence(spec.Anim.ToString()), "Use exactly " + spec.Folder.ToUpperInvariant() + "_1 through " + spec.Folder.ToUpperInvariant() + "_5, followed by .png or .webm. Uppercase names are required.", null));
                }),
                LegalCategory(),
                CreditsCategory(),
                new Category("Troubleshooting", "method-logs", 0x636366, null, stack =>
                {
                    var log = new FlatButton("Open log", 112);
                    log.Click += (s, e) =>
                    {
                        try { System.Diagnostics.Process.Start(Path.Combine(Program.Data, "opagachi.log")); }
                        catch (Exception ex) { ShowStatus("Couldn't open the log: " + ex.Message, true); }
                    };
                    AddRow(stack, "Diagnostic log", "Events, detection and performance, for when something misbehaves", log);
                }));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            refresh.Stop();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refresh.Dispose();
                foreach (var img in textureCache.Values) if (img != null) img.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // The agent's name under its pet on the Agents page: drawn like the pet's own name tag (its colour or gradient
    // with a dark outline), so the name colour previews exactly.
    sealed class NameLabel : Control
    {
        string style = NameFill.White;

        public NameLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Heading(14F);
        }

        public string Style { get { return style; } set { style = value; Invalidate(); } }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                path.AddString(Text, Font.FontFamily, (int)Font.Style, Font.SizeInPoints * 96f / 72f, ClientRectangle, format);
                using (var outline = new Pen(Color.FromArgb(220, 0, 0, 0), 2.5f) { LineJoin = LineJoin.Round }) g.DrawPath(outline, path);
                using (var b = NameFill.Brush(style, path.GetBounds())) g.FillPath(b, path);
            }
        }
    }

    // One row of the Connections list (Gemini Response 3, 4.8): the name with its method pill 8 px to the
    // right (hover explains it), status dot + text, and one fixed 108×28 action: primary for one-click
    // upgrades, secondary for removals, disabled when there's nothing to act on.
    sealed class ConnectionRow : ListRow
    {
        public readonly FlatButton Action = new FlatButton("", 112);
        readonly StatusDot dot = new StatusDot();
        readonly Label state = new Label();
        string title = "", badge = "", icon = "method-app";
        Rectangle pill = Rectangle.Empty;
        public Connection Item;

        public ConnectionRow()
        {
            Dock = DockStyle.Fill;
            Height = 54;
            ShowBar = false;
            dot.Location = new Point(12, 29);
            state.Location = new Point(28, 29);
            state.Size = new Size(380, 18);
            state.AutoEllipsis = true;
            state.BackColor = Color.Transparent;
            state.Font = Theme.Ui(8.75F);
            Action.Location = new Point(540 - 16 - 112, 13);
            Resize += (s, e) => Action.Left = Width - 16 - Action.Width;
            Controls.Add(dot);
            Controls.Add(state);
            Controls.Add(Action);
            Tip.Attach(this, p => Item != null && pill.Contains(p) && !string.IsNullOrEmpty(Item.BadgeTip) ? new TipRegion(Item.BadgeTip, pill) : null);
        }

        public void Set(Connection c, Color tie)
        {
            Item = c;
            if (title != c.Title || badge != c.Badge || icon != c.Icon || Tie.ToArgb() != tie.ToArgb())
            {
                title = c.Title; badge = c.Badge; icon = c.Icon; Tie = tie;
                Invalidate();
            }
            dot.Kind = c.Dot;
            if (state.Text != c.State) state.Text = c.State;
            state.ForeColor = c.Dot == DotKind.Error ? Theme.Danger : c.Dot == DotKind.NotInstalled || c.Dot == DotKind.Off ? Theme.TextMuted : Theme.TextSecondary;
            if (Action.Text != c.Action) Action.Text = c.Action;
            Action.Enabled = c.ActionEnabled;
            Action.Primary = c.ActionPrimary && c.ActionEnabled;
            Action.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            bool muted = Item != null && Item.Dot == DotKind.NotInstalled;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var semibold = Theme.Semibold(9.5F))
            {
                var size = TextRenderer.MeasureText(g, title, semibold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, title, semibold, new Point(16, 8), muted ? Theme.TextMuted : Theme.TextPrimary, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                using (var small = Theme.Ui(7.5F))
                {
                    var bsize = TextRenderer.MeasureText(g, badge, small, Size.Empty, TextFormatFlags.NoPadding);
                    pill = new Rectangle(16 + size.Width + 8, 7, bsize.Width + 16, 18);
                    var r = new RectangleF(pill.X + 0.5f, pill.Y + 0.5f, pill.Width - 1, pill.Height - 1);
                    using (var b = new SolidBrush(Theme.TrackBottom)) using (var p = Theme.Round(r, r.Height / 2)) g.FillPath(b, p);
                    using (var pen = new Pen(Theme.Border)) using (var p = Theme.Round(r, r.Height / 2)) g.DrawPath(pen, p);
                    TextRenderer.DrawText(g, badge, small, new Rectangle(pill.X + 8, pill.Y, bsize.Width + 4, pill.Height), Theme.TextSecondary,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }
    }

    // One question (user, 2026-11-01): click it (or Space/Enter) and the answer unfolds underneath on the
    // Smooth spring while the chevron turns from › to ⌄; click again, even mid-way, to fold it back. The
    // card draws the hairlines between questions.
    sealed class FaqItem : Control, IAnimated
    {
        const int Inset = 16, Pad = 12, ChevronSpace = 36;
        readonly string question, answer;
        readonly Spring open = new Spring(0, Feel.Smooth);
        readonly Fade hover = new Fade(Theme.StandardMs);
        readonly Font questionFont = Theme.Semibold(10F), answerFont = Theme.Ui(9.5F);
        int questionH, answerH, measuredFor = -1;

        public FaqItem(string question, string answer)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                     | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            this.question = question;
            this.answer = answer;
            Dock = DockStyle.Fill;
            AutoSize = true;   // the page's table asks GetPreferredSize only of auto-sized controls
            Margin = new Padding(0);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.OutlineButton;
            AccessibleName = question;
            Measure(540);
        }

        int TextWidth(int width) { return Math.Max(120, width - Inset - ChevronSpace - 16); }

        void Measure(int width)
        {
            if (width == measuredFor) return;
            measuredFor = width;
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            questionH = TextRenderer.MeasureText(question, questionFont, new Size(TextWidth(width), 0), flags).Height;
            answerH = TextRenderer.MeasureText(answer, answerFont, new Size(TextWidth(width), 0), flags).Height;
        }

        int Closed { get { return questionH + 2 * Pad; } }
        int Shown { get { return Closed + (int)Math.Round((answerH + Pad) * Theme.Clamp01(open.Value)); } }

        // Measured at the width the page offers (a 0 width before the first layout would wrap every word).
        public override Size GetPreferredSize(Size proposed)
        {
            int w = proposed.Width >= 200 && proposed.Width < 10000 ? proposed.Width : Width >= 200 ? Width : 540;   // ignore tiny probe widths: they wrap every word
            Measure(w);
            return new Size(w, Shown);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width <= 0) return;
            int before = Shown;
            Measure(Width);
            if (Shown != before) Relayout();
        }

        // The card and the page column both size to their rows: lay out every level up to the scrolling page.
        void Relayout()
        {
            for (var p = Parent; p != null && !(p is GlassScroller); p = p.Parent) p.PerformLayout();
        }

        void Toggle()
        {
            open.Target = open.Target > 0.5 ? 0 : 1;
            Animator.Start(this);
        }

        public bool Step(double dt)
        {
            int before = Shown;
            bool moving = open.Step(dt) | hover.Step(dt);
            if (Shown != before) Relayout();
            Invalidate();
            return moving;
        }

        protected override void OnClick(EventArgs e) { Focus(); Toggle(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Toggle(); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnMouseEnter(EventArgs e) { hover.On = true; Animator.Start(this); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.On = false; Animator.Start(this); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double h = Theme.EaseOut(hover.T), k = Theme.Clamp01(open.Value);
            if (h > 0.01) using (var b = new SolidBrush(Theme.Mix(Color.FromArgb(0, Theme.RowHoverTint), Theme.RowHoverTint, h))) using (var p = Theme.Round(new RectangleF(6, 2, Width - 12, Closed - 4), 10)) g.FillPath(b, p);
            int textW = TextWidth(Width);
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, question, questionFont, new Rectangle(Inset, Pad, textW, questionH), Theme.TextPrimary, flags);
            if (Focused && ShowFocusCues)
                using (var p = new Pen(Theme.Accent)) using (var path = Theme.Round(new RectangleF(Inset - 6, Pad - 4, textW + 12, questionH + 8), 6)) g.DrawPath(p, path);

            // Chevron centred on the first line of the question, turning 0 → 90°.
            float cx = Width - 16 - 10, cy = Pad + questionFont.Height / 2f;
            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform((float)(90 * k));
            using (var pen = Icons.RoundPen(Theme.Mix(Theme.TextSecondary, Theme.Accent, k), 1.6f))
                g.DrawLines(pen, new[] { new PointF(-2.5f, -5), new PointF(2.5f, 0), new PointF(-2.5f, 5) });
            g.Restore(state);

            if (k > 0.01)
            {
                // The answer fades in as it unfolds, rising 6 px into place.
                var color = Theme.Mix(Theme.Surface, Theme.TextSecondary, Theme.Clamp01(k * 1.4));
                int dy = Theme.ReduceMotion ? 0 : (int)Math.Round(6 * (1 - k));
                var old = g.Clip;
                g.SetClip(new Rectangle(0, Closed - Pad / 2, Width, Height));
                TextRenderer.DrawText(g, answer, answerFont, new Rectangle(Inset, Closed - Pad / 2 + dy, textW, answerH), color, flags);
                g.Clip = old;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { questionFont.Dispose(); answerFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // A short note that's always open (Credits): a semibold title over wrapping secondary text. With a link, the
    // text is in the accent colour and a click opens it in the browser.
    sealed class NoteItem : Control
    {
        const int Inset = 16, Pad = 11;
        readonly string title, body, link;
        readonly Font titleFont = Theme.Semibold(9.5F), bodyFont = Theme.Ui(9.5F);
        int titleH, bodyH, measuredFor = -1;

        public NoteItem(string title, string body, string link)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            this.title = title;
            this.body = body;
            this.link = link;
            Dock = DockStyle.Fill;
            AutoSize = true;
            Margin = new Padding(0);
            BackColor = Color.Transparent;
            AccessibleName = title + ": " + body;
            if (link != null) { Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.Link; }
        }

        void Measure(int width)
        {
            if (width == measuredFor) return;
            measuredFor = width;
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            int w = Math.Max(120, width - 2 * Inset);
            titleH = TextRenderer.MeasureText(title, titleFont, new Size(w, 0), flags).Height;
            bodyH = TextRenderer.MeasureText(body, bodyFont, new Size(w, 0), flags).Height;
        }

        public override Size GetPreferredSize(Size proposed)
        {
            int w = proposed.Width >= 200 && proposed.Width < 10000 ? proposed.Width : Width >= 200 ? Width : 540;   // ignore tiny probe widths
            Measure(w);
            return new Size(w, Pad + titleH + 2 + bodyH + Pad);
        }

        protected override void OnClick(EventArgs e)
        {
            if (link != null) try { System.Diagnostics.Process.Start(link); } catch (Exception ex) { Log.Write("link: " + ex.Message); }
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Measure(Width);
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            int w = Math.Max(120, Width - 2 * Inset);
            TextRenderer.DrawText(e.Graphics, title, titleFont, new Rectangle(Inset, Pad, w, titleH), Theme.TextPrimary, flags);
            TextRenderer.DrawText(e.Graphics, body, bodyFont, new Rectangle(Inset, Pad + titleH + 2, w, bodyH), link != null ? Theme.Accent : Theme.TextSecondary, flags);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { titleFont.Dispose(); bodyFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // Footer status (Gemini Responses 3 and 5): the old line rises 8 px and fades in 100 ms; the new one (with
    // a 6 px dot, red for errors) rises 8 px into place on the Gentle spring, fading in over 150 ms. Back to
    // the default (the live summary) after 4 s (8 s for errors).
    sealed class FadeLabel : Control, IAnimated
    {
        public string Default = "";
        string text, old = "";
        bool error, oldError;
        double ms = 1000, held;
        static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        public FadeLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Ui(9F);
        }

        string Current { get { return text ?? Default; } }

        public void Say(string message, bool isError)
        {
            old = Current;
            oldError = error;
            text = message;
            error = isError;
            ms = 0;
            held = Clock.Elapsed.TotalSeconds;
            Animator.Start(this);
        }

        public bool Step(double dt)
        {
            ms += dt * 1000;
            Invalidate();
            if (ms < 30 + Feel.Gentle.Seconds * 1000) return true;
            if (text == null) return false;
            if (Clock.Elapsed.TotalSeconds - held > (error ? Theme.FooterErrorHoldMs : Theme.FooterHoldMs) / 1000.0) { Say(null, false); }
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int lift = Theme.ReduceMotion ? 0 : 8;
            if (ms < Theme.FastMs) Line(g, old, oldError, 1 - ms / Theme.FastMs, -(int)Math.Round(lift * Feel.Gentle.Progress(ms / 1000)), old == Default);
            Line(g, Current, error, Theme.Clamp01((ms - 30) / Theme.StandardMs), (int)Math.Round(lift * (1 - Feel.Gentle.Progress((ms - 30) / 1000))), text == null);
        }

        void Line(Graphics g, string t, bool isError, double alpha, int dy, bool isDefault)
        {
            if (alpha <= 0.01 || string.IsNullOrEmpty(t)) return;
            int x = 0;
            if (!isDefault)
            {
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * alpha), isError ? Theme.Danger : Theme.Success))) g.FillEllipse(b, 0, Height / 2f - 3 + dy, 6, 6);
                x = 13;
            }
            var color = Theme.Mix(Theme.Canvas, isError ? Theme.Danger : isDefault ? Theme.TextSecondary : Theme.TextPrimary, alpha);
            TextRenderer.DrawText(g, t, Font, new Rectangle(x, dy, Width - x, Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Page switch (Gemini Response 5): a page comes from the side of its tab. Picking a tab to the right
    // slides the old page's snapshot up and out while the new one rises 16 px into place on the Smooth
    // spring (`dir` +1), cross-fading over 250 ms; a tab to the left does the same downwards (`dir` -1).
    // Opening a category slides sideways instead, 40 px, like iOS (`horizontal`; Back goes the other way).
    sealed class PageTransition : Control, IAnimated
    {
        readonly int travel;
        readonly bool horizontal;
        readonly Bitmap before;
        readonly int dir;
        Bitmap after;
        double seconds;

        public PageTransition(Bitmap before, int dir, bool horizontal)
        {
            this.horizontal = horizontal;
            travel = horizontal ? 40 : 16;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            this.before = before;
            this.dir = dir;
            Dock = DockStyle.Fill;
        }

        public void Start(Bitmap next)
        {
            after = next;
            Animator.Start(this);
        }

        public bool Step(double dt)
        {
            seconds += dt;
            Invalidate();
            // Done once the fade is over and the slide is under half a pixel: the snapshot blocks clicks.
            if (seconds * 1000 < Theme.CrossfadeMs || travel * (1 - Feel.Smooth.Progress(seconds)) >= 0.5) return true;
            var host = Parent;
            if (host != null) host.Controls.Remove(this);
            Dispose();
            return false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            double k = Feel.Smooth.Progress(seconds), a = Theme.Clamp01(seconds * 1000 / Theme.CrossfadeMs);
            Blit(g, before, (int)Math.Round(-dir * travel * k), 1 - a);
            if (after != null) Blit(g, after, (int)Math.Round(dir * travel * (1 - k)), a);
        }

        void Blit(Graphics g, Bitmap bmp, int shift, double alpha)
        {
            if (alpha <= 0.01) return;
            var dest = new Rectangle(horizontal ? shift : 0, horizontal ? 0 : shift, bmp.Width, bmp.Height);
            if (alpha >= 0.99) { g.DrawImage(bmp, dest); return; }
            using (var attrs = new ImageAttributes())
            {
                attrs.SetColorMatrix(new ColorMatrix { Matrix33 = (float)alpha });
                g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { before.Dispose(); if (after != null) after.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
