using System.Diagnostics;
using System.Drawing;

namespace Bunker;

/// <summary>Settings with a vertical submenu: Servers, General, Updates, About.</summary>
public partial class SettingsPage : Panel, ISectionPage
{
    private readonly Core _core;
    private readonly Dictionary<string, (TabButton Button, Control Panel)> _sections = new();
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(0, 14, 0, 0) };

    // Servers
    private readonly DarkGrid _grid = new();
    private readonly Button _add = Theme.PrimaryButton("Add server");
    private readonly Button _import = Theme.GhostButton("Import...");
    private readonly Button _edit = Theme.GhostButton("Edit");
    private readonly Button _remove = Theme.GhostButton("Remove");
    private readonly Button _use = Theme.GhostButton("Connect");

    // Updates
    private readonly Button _check = Theme.PrimaryButton("Check for updates");
    private readonly ProgressBar _progress = new() { Width = 420, Height = 6, Visible = false, Location = new Point(0, 132) };
    private readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = Theme.Muted, Location = new Point(0, 148) };

    public SettingsPage(Core core)
    {
        _core = core;
        BackColor = Theme.Bg;

        var menu = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Panel };
        menu.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawRectangle(p, 0, 0, menu.Width - 1, menu.Height - 1); };

        var defs = new (string Key, string Glyph, string Title, Func<Control> Build)[]
        {
            ("servers", "", "Servers", BuildServers),
            ("general", "", "General", BuildGeneral),
            ("console", "", "Console", BuildConsole),
            ("chat", "", "Chat", BuildChat),
            ("updates", "", "Updates", BuildUpdates),
            ("about", "", "About", BuildAbout),
        };
        foreach (var d in defs)
        {
            var panel = d.Build();
            panel.Dock = DockStyle.Fill;
            panel.Visible = false;
            _content.Controls.Add(panel);
            var key = d.Key;
            var button = new TabButton(d.Glyph, d.Title) { Dock = DockStyle.Left };
            button.Click += (_, _) => ShowSection(key);
            _sections[key] = (button, panel);
        }
        foreach (var d in defs.Reverse()) menu.Controls.Add(_sections[d.Key].Button);

        var body = new Panel { BackColor = Theme.Bg };
        body.Controls.Add(_content);
        body.Controls.Add(menu);

        var page = new Page("Settings", "Your servers, startup behaviour and updates", body);
        page.Dock = DockStyle.Fill;
        Controls.Add(page);

        _core.ServerChanged += RefreshServers;
        _core.StateChanged += RefreshServers;
        RefreshServers();
        ShowSection("servers");
    }

    public void ShowSection(string key)
    {
        foreach (var (k, s) in _sections) { s.Panel.Visible = k == key; s.Button.Selected = k == key; }
    }

    private static Card Wrap(string title, Control inner) => Card.Titled(title, inner);

    // ---------------------------------------------------------------- Servers

    private Control BuildServers()
    {
        _grid.Columns.Add("Name", "Name");
        _grid.Columns.Add("Address", "Address");
        _grid.Columns.Add("Status", "Status");
        _grid.Columns[0].FillWeight = 120;
        _grid.Columns[1].FillWeight = 110;
        _grid.Columns[2].FillWeight = 70;
        _grid.Dock = DockStyle.Fill;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(0, 12, 0, 0), BackColor = Theme.Panel };
        foreach (var b in new[] { _add, _import, _edit, _remove, _use }) { b.Height = 34; b.Margin = new Padding(0, 0, 8, 0); bar.Controls.Add(b); }

        var hint = new Label
        {
            Text = "Add as many servers as you like, or import them from RustAdmin. Click the server name in the sidebar to switch between them.",
            Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(0, 4, 0, 0),
        };

        var inner = new Panel { BackColor = Theme.Panel };
        inner.Controls.Add(_grid);
        inner.Controls.Add(bar);
        inner.Controls.Add(hint);

        _add.Click += (_, _) => AddServer();
        _import.Click += (_, _) => ImportServers();
        _edit.Click += (_, _) => EditServer();
        _remove.Click += async (_, _) => await RemoveServerAsync();
        _use.Click += async (_, _) => { if (SelectedServer() is { } p) await _core.SwitchServerAsync(p.Id); };
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditServer(); };
        return Wrap("Servers", inner);
    }

    private ServerProfile? SelectedServer() =>
        _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as ServerProfile : null;

    private void RefreshServers()
    {
        var selectedId = SelectedServer()?.Id;
        var activeId = _core.Settings.ActiveServer?.Id;
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var s in _core.Settings.Servers)
        {
            var status = s.Id != activeId ? "" : _core.State == ConnState.Connected ? "Active - connected" : "Active";
            var i = _grid.Rows.Add(s.DisplayName, $"{s.Host}:{s.Port}", status);
            _grid.Rows[i].Tag = s;
            if (s.Id == activeId) _grid.Rows[i].Cells[2].Style.ForeColor = _core.State == ConnState.Connected ? Theme.Ok : Theme.Accent;
            if (s.Id == (selectedId ?? activeId)) _grid.Rows[i].Selected = true;
        }
        _grid.ResumeLayout();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var p = SelectedServer();
        _edit.Enabled = _remove.Enabled = p is not null;
        var isActive = p is not null && p.Id == _core.Settings.ActiveServer?.Id;
        _use.Enabled = p is not null && !(isActive && _core.State != ConnState.Disconnected);
        _use.Text = isActive ? "Connect" : "Switch to";
    }

    private void AddServer()
    {
        using var f = new OnboardingForm(null, firstRun: false);
        if (f.ShowDialog(FindForm()) == DialogResult.OK)
            _core.SaveServer(f.Result!, makeActive: _core.Settings.Servers.Count == 0);
    }

    private void ImportServers()
    {
        using var f = new ImportServersForm(_core);
        if (f.ShowDialog(FindForm()) != DialogResult.OK) return;
        foreach (var server in f.Result) _core.SaveServer(server, makeActive: _core.Settings.Servers.Count == 0);
    }

    private void EditServer()
    {
        if (SelectedServer() is not { } p) return;
        using var f = new OnboardingForm(p, firstRun: false);
        if (f.ShowDialog(FindForm()) != DialogResult.OK) return;
        _core.SaveServer(f.Result!);
        if (p.Id == _core.Settings.ActiveServer?.Id && _core.State == ConnState.Connected)
            _core.Log.Add("Server details changed. Disconnect and connect again to use them.", LogKind.Info);
    }

    private async Task RemoveServerAsync()
    {
        if (SelectedServer() is not { } p) return;
        var answer = MessageBox.Show(FindForm(),
            $"Remove \"{p.DisplayName}\" from Bunker?\n\nThis only forgets the saved connection details. Your game server is not affected.",
            "Remove server", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes) await _core.DeleteServerAsync(p.Id);
    }

    // ---------------------------------------------------------------- General

    private Control BuildGeneral()
    {
        var inner = new Panel { BackColor = Theme.Panel };

        // Theme: dark or light. The colours are chosen once when Bunker starts, so a change needs a restart.
        inner.Controls.Add(new Label { Text = "Theme", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(2, 6) });
        var dark = new RadioButton { Text = "Dark", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Location = new Point(0, 28), Checked = !Theme.IsLight };
        var light = new RadioButton { Text = "Light", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Location = new Point(90, 28), Checked = Theme.IsLight };
        void ThemeChanged(bool toLight)
        {
            if (_core.Settings.LightTheme == toLight) return;
            _core.Settings.LightTheme = toLight;
            _core.Settings.Save();
            if (toLight == Theme.IsLight) return;
            var restart = MessageBox.Show(FindForm(), "Restart Bunker now to switch theme?\n\nChoose No to switch the next time Bunker starts.",
                "Theme changed", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (restart == DialogResult.Yes)
            {
                Environment.SetEnvironmentVariable("BUNKER_RESTARTING", "1"); // the new copy waits for this one to exit instead of being turned away
                Application.Restart();
            }
        }
        dark.CheckedChanged += (_, _) => { if (dark.Checked) ThemeChanged(false); };
        light.CheckedChanged += (_, _) => { if (light.Checked) ThemeChanged(true); };
        inner.Controls.Add(dark);
        inner.Controls.Add(light);

        inner.Controls.Add(Check("Start Bunker maximised", _core.Settings.StartMaximised, 72, v => _core.Settings.StartMaximised = v));
        inner.Controls.Add(Check("Connect to the active server when Bunker starts", _core.Settings.AutoConnect, 106, v => _core.Settings.AutoConnect = v));
        inner.Controls.Add(Check("Keep the Rust item list and pictures up to date from GitHub (used by item search)", _core.Settings.UseOnlineItemList, 140, v => _core.Settings.UseOnlineItemList = v));
        inner.Controls.Add(Check("Reconnect automatically if the connection to the server drops", _core.Settings.AutoReconnect, 174, v => _core.Settings.AutoReconnect = v));
        void TrayChanged(Action<bool> set, bool v) { set(v); _core.NotifyTraySettingsChanged(); }
        inner.Controls.Add(Check("Show the Bunker icon in the system tray (right-click it to show, connect or quit)", _core.Settings.ShowTrayIcon, 208, v => TrayChanged(x => _core.Settings.ShowTrayIcon = x, v)));
        inner.Controls.Add(Check("Windows notifications for chat watch-words and a lost connection while Bunker is not in front", _core.Settings.TrayNotifications, 242, v => TrayChanged(x => _core.Settings.TrayNotifications = x, v)));
        inner.Controls.Add(Check("Minimising hides Bunker from the taskbar, leaving only the tray icon", _core.Settings.TrayHideWhenMinimised, 276, v => TrayChanged(x => _core.Settings.TrayHideWhenMinimised = x, v)));
        inner.Controls.Add(Check("Look players up on Steam when I open their card (sends only their Steam ID to steamcommunity.com)", _core.Settings.SteamLookups, 310, v => _core.Settings.SteamLookups = v));
        inner.Controls.Add(new Label { Text = "Steam Web API key (optional, free): adds Rust hours, account age and the full ban record to player cards", ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Location = new Point(2, 348) });
        var keyField = new ModernField { Width = 360, Location = new Point(0, 368), Text = _core.Settings.SteamApiKey };
        keyField.Box.UseSystemPasswordChar = true;
        keyField.Box.PlaceholderText = "32 letters and numbers";
        var keyShow = new Label { Text = "Show", ForeColor = Theme.Muted, Width = 46, TextAlign = ContentAlignment.MiddleRight, Cursor = Cursors.Hand, Font = Theme.Small };
        keyField.AddTrailing(keyShow);
        keyShow.Click += (_, _) => { keyField.Box.UseSystemPasswordChar = !keyField.Box.UseSystemPasswordChar; keyShow.Text = keyField.Box.UseSystemPasswordChar ? "Show" : "Hide"; };
        var keyHelp = Link("Get a free key", "https://steamcommunity.com/dev/apikey", 378);
        keyHelp.Left = keyField.Right + 14;
        var keyStatus = new Label { Font = Theme.Small, AutoSize = true, Location = new Point(2, 408) };
        void CheckKey()
        {
            var text = keyField.Text.Trim();
            _core.Settings.SteamApiKey = text;
            _core.Settings.Save();
            keyStatus.Text = text.Length == 0 ? "" : SteamLookup.LooksLikeKey(text) ? "Saved. Player cards will use it." : "A Steam key is 32 letters and numbers.";
            keyStatus.ForeColor = SteamLookup.LooksLikeKey(text) ? Theme.Ok : Theme.Warn;
        }
        keyField.Box.TextChanged += (_, _) => CheckKey();
        inner.Controls.Add(keyField);
        inner.Controls.Add(keyHelp);
        inner.Controls.Add(keyStatus);
        inner.Controls.Add(new Label { Text = "When I click the X in the corner of the window", ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Location = new Point(2, 442) });
        var closeChoices = new FlowLayoutPanel { Location = new Point(0, 462), Size = new Size(520, 30), BackColor = Theme.Panel, WrapContents = false };
        void AddCloseChoice(string text, string value)
        {
            var radio = new RadioButton { Text = text, AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 24, 0), Checked = CloseButtonSetting.Normalise(_core.Settings.CloseButton) == value };
            radio.CheckedChanged += (_, _) => { if (radio.Checked) { _core.Settings.CloseButton = value; _core.Settings.Save(); } };
            closeChoices.Controls.Add(radio);
        }
        AddCloseChoice("Ask me", CloseButtonSetting.Ask);
        AddCloseChoice("Minimise Bunker", CloseButtonSetting.Minimise);
        AddCloseChoice("Close Bunker", CloseButtonSetting.Close);
        inner.Controls.Add(closeChoices);
        inner.Controls.Add(new Label { Text = "Theme, maximised and connect settings take effect the next time Bunker starts.", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(2, 506) });
        return Wrap("General", inner);
    }

    private CheckBox Check(string text, bool value, int top, Action<bool> set)
    {
        var c = new CheckBox { Text = text, AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Location = new Point(0, top), Checked = value };
        c.CheckedChanged += (_, _) => { set(c.Checked); _core.Settings.Save(); };
        return c;
    }

    // ---------------------------------------------------------------- Updates

    private Control BuildUpdates()
    {
        var inner = new Panel { BackColor = Theme.Panel, AutoScroll = true };
        DarkScroll.Apply(inner);
        inner.Controls.Add(new Label { Text = $"Current version: {Updater.VersionText}", AutoSize = true, ForeColor = Theme.Muted, Location = new Point(0, 6) });
        if (Updater.IsStoreVersion)
        {
            inner.Controls.Add(new Label { Text = "This copy of Bunker comes from the Microsoft Store, which keeps it up to date for you.", AutoSize = true, ForeColor = Theme.Text, Location = new Point(0, 40) });
            AddWidgetUpdates(inner, 96);
            return Wrap("Updates", inner);
        }
        inner.Controls.Add(Check("Check for updates when Bunker starts (you are asked before anything installs)", _core.Settings.CheckForUpdatesOnLaunch, 36, v => _core.Settings.CheckForUpdatesOnLaunch = v));
        _check.Location = new Point(0, 76);
        inner.Controls.Add(_check);
        inner.Controls.Add(_progress);
        inner.Controls.Add(_result);
        _check.Click += async (_, _) => await CheckForUpdateAsync();
        AddWidgetUpdates(inner, 190);
        return Wrap("Updates", inner);
    }

    // ---- Widget updates: newer versions of the community widgets you have installed

    private readonly FlowLayoutPanel _widgetRows = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Panel, Margin = new Padding(0) };
    private readonly Label _widgetStatus = new() { AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Theme.Muted, Font = Theme.Small };
    private readonly Button _widgetCheck = Theme.GhostButton("Check for widget updates");

    private void AddWidgetUpdates(Panel inner, int top)
    {
        inner.Controls.Add(new Panel { Height = 1, Width = 640, BackColor = Theme.Border, Location = new Point(0, top) });
        inner.Controls.Add(new Label { Text = "Widget updates", Font = Theme.Semibold, ForeColor = Theme.Text, AutoSize = true, Location = new Point(0, top + 16) });
        inner.Controls.Add(new Label
        {
            Text = "Community widgets are updated separately from Bunker. A newer version is also marked with an orange dot in the WIDGETS menu.",
            AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Theme.Muted, Font = Theme.Small, Location = new Point(0, top + 42),
        });
        _widgetCheck.Location = new Point(0, top + 82);
        _widgetCheck.Width = 220;
        _widgetCheck.Height = 36;
        inner.Controls.Add(_widgetCheck);
        _widgetStatus.Location = new Point(236, top + 91);
        inner.Controls.Add(_widgetStatus);
        _widgetRows.Location = new Point(0, top + 132);
        inner.Controls.Add(_widgetRows);

        _widgetCheck.Click += async (_, _) => await _core.WidgetUpdates.CheckAsync(force: true);
        _core.WidgetUpdates.Changed += () => { if (IsHandleCreated) BeginInvoke(RefreshWidgetUpdates); };
        _core.Widgets.Changed += () => { if (IsHandleCreated) BeginInvoke(RefreshWidgetUpdates); };
        RefreshWidgetUpdates();
    }

    private void RefreshWidgetUpdates()
    {
        var service = _core.WidgetUpdates;
        _widgetCheck.Enabled = !service.Checking;
        foreach (Control c in _widgetRows.Controls.Cast<Control>().ToList()) { _widgetRows.Controls.Remove(c); c.Dispose(); }

        if (service.Checking) { _widgetStatus.ForeColor = Theme.Muted; _widgetStatus.Text = "Checking the widget library..."; return; }
        if (service.LastError is { } error) { _widgetStatus.ForeColor = Theme.Err; _widgetStatus.Text = "Could not check: " + error; return; }
        if (_core.Widgets.All.Count == 0) { _widgetStatus.ForeColor = Theme.Muted; _widgetStatus.Text = "You have no community widgets installed."; return; }
        if (service.Available.Count == 0)
        {
            _widgetStatus.ForeColor = service.LastChecked is null ? Theme.Muted : Theme.Ok;
            _widgetStatus.Text = service.LastChecked is { } at ? $"All your widgets are up to date (checked {at:HH:mm})." : "Not checked yet. Press the button to look for updates.";
            return;
        }

        _widgetStatus.ForeColor = Theme.Warn;
        _widgetStatus.Text = $"{service.Available.Count} widget update{(service.Available.Count == 1 ? "" : "s")} available.";
        foreach (var update in service.Available)
        {
            var row = new Panel { Width = 640, Height = 52, BackColor = Theme.Input, Margin = new Padding(0, 0, 0, 8) };
            row.Controls.Add(new Label { Text = update.Installed.Name, Font = Theme.Semibold, ForeColor = Theme.Text, AutoSize = true, Location = new Point(14, 8) });
            row.Controls.Add(new Label { Text = $"Update Available:  v{update.Installed.Version}  →  v{update.Offered.Version}", Font = Theme.Small, ForeColor = Theme.Accent, AutoSize = true, Location = new Point(14, 29) });
            var go = Theme.PrimaryButton("Review and update");
            go.Size = new Size(170, 36);
            go.Location = new Point(row.Width - 184, 8);
            var chosen = update;
            go.Click += (_, _) => service.ReviewAndInstall(chosen, FindForm());
            row.Controls.Add(go);
            _widgetRows.Controls.Add(row);
        }
    }

    private async Task CheckForUpdateAsync()
    {
        _check.Enabled = false;
        _result.ForeColor = Theme.Muted;
        _result.Text = "Checking...";
        try
        {
            var update = await Updater.CheckAsync();
            if (update is null) { _result.ForeColor = Theme.Ok; _result.Text = "You're up to date."; return; }

            var answer = MessageBox.Show(FindForm(),
                $"Version {update.Version} is available (you have {Updater.CurrentVersion}).\n\nInstall now? Bunker will restart.",
                "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) { _result.Text = $"Version {update.Version} is available."; return; }

            _progress.Visible = true;
            _result.Text = "Downloading...";
            await Updater.DownloadAndLaunchAsync(update, new Progress<int>(p => _progress.Value = p));
            Application.Exit();
        }
        catch (Exception e)
        {
            _result.ForeColor = Theme.Err;
            _result.Text = "Update check failed: " + e.Message;
        }
        finally { _check.Enabled = true; }
    }

    // ---------------------------------------------------------------- About

    private Control BuildAbout()
    {
        var inner = new Panel { BackColor = Theme.Panel };
        if (SplashForm.LoadLogo() is { } logo)
            inner.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(72, 72), Location = new Point(0, 6), BackColor = Theme.Panel });
        inner.Controls.Add(new Label { Text = "Bunker", Font = new Font("Segoe UI Semibold", 16f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(86, 8) });
        inner.Controls.Add(new Label { Text = Updater.VersionText, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(88, 42) });
        inner.Controls.Add(new Label
        {
            Text = "Manage your Rust game server from your desktop: watch the console, see who is online and run commands without being in the game.",
            ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(520, 0), Location = new Point(0, 96),
        });

        var github = Link("Project page on GitHub", $"https://github.com/{Updater.Repo}", 150);
        var releases = Link("All releases and downloads", $"https://github.com/{Updater.Repo}/releases", 176);
        inner.Controls.Add(github);
        inner.Controls.Add(releases);

        var notes = Theme.GhostButton("What's new");
        notes.Location = new Point(0, 216);
        notes.Click += (_, _) =>
        {
            using var form = new WhatsNewForm(WhatsNew.Latest(WhatsNew.Load(), Updater.CurrentVersion), "What's new in Bunker");
            form.ShowDialog(FindForm());
        };
        inner.Controls.Add(notes);
        var folder = Theme.GhostButton("Open settings folder");
        folder.Location = new Point(notes.Width + 10, 216);
        folder.Click += (_, _) => { try { Directory.CreateDirectory(AppSettings.Folder); Process.Start(new ProcessStartInfo(AppSettings.Folder) { UseShellExecute = true }); } catch { } };
        inner.Controls.Add(folder);
        return Wrap("About", inner);
    }

    private static LinkLabel Link(string text, string url, int top)
    {
        var l = new LinkLabel
        {
            Text = text, AutoSize = true, Location = new Point(0, top), LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover,
            VisitedLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline,
        };
        l.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
        return l;
    }
}
