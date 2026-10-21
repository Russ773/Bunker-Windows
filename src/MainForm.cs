using System.Drawing;

namespace Bunker;

public class MainForm : Form
{
    private readonly Core _core = new(AppSettings.Load());
    private readonly Sidebar _sidebar;
    private readonly Dictionary<string, Control> _pages = new();
    private TrayService? _tray;

    public MainForm()
    {
        Theme.Apply(this);
        Text = "Bunker";
        Size = new Size(1280, 780);
        MinimumSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;

        if (_core.Settings.StartMaximised) WindowState = FormWindowState.Maximized;
        _sidebar = new Sidebar(_core);
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

        _pages["dashboard"] = Pages.Dashboard(_core);
        _pages["console"] = Pages.Console(_core);
        _pages["players"] = Pages.Players(_core);
        var servers = new ServersView(_core);
        servers.OpenRequested += (id, page) => { _ = _core.SwitchServerAsync(id); Navigate(page); };
        _pages["servers"] = new Page("Multi Server", "Up to six of your servers at a glance. Bunker keeps a light connection to each one while this page is open", servers);
        _pages["layout"] = new LayoutPage(_core);
        var widgets = new WidgetsPage(_core);
        widgets.OpenRequested += key => Navigate("widgets:" + key);
        _pages["widgets"] = widgets;
        _pages["settings"] = new SettingsPage(_core);
        foreach (var page in _pages.Values)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            content.Controls.Add(page);
        }

        Controls.Add(content);
        Controls.Add(_sidebar);

        _sidebar.Navigated += Navigate;
        _sidebar.ServerSwitchRequested += id => _ = _core.SwitchServerAsync(id);
        _sidebar.ConnectClicked += async () => await ToggleConnectionAsync();
        Navigate("dashboard");
#if DEBUG
        // Test hooks (debug builds only): open on a given page, and keep the window off-screen.
        if (Environment.GetEnvironmentVariable("BUNKER_START_PAGE") is { Length: > 0 } startPage && _pages.ContainsKey(startPage.Split(':')[0])) Navigate(startPage);
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_SIZE") is { Length: > 0 } size && size.Split('x') is [var sw, var sh] && int.TryParse(sw, out var w0) && int.TryParse(sh, out var h0)) { WindowState = FormWindowState.Normal; Size = new Size(w0, h0); }
        if (Environment.GetEnvironmentVariable("BUNKER_OFFSCREEN") == "1") { WindowState = FormWindowState.Normal; StartPosition = FormStartPosition.Manual; Location = new Point(-4000, 100); ShowInTaskbar = false; }
#endif

        Shown += async (_, _) => await StartupAsync();
#if DEBUG
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_HISTORY") == "1")
            Shown += async (_, _) => { await Task.Delay(5500); _core.Monitor.SeedForTest(); };
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_REVIEW") is { Length: > 0 } reviewFile)
            Shown += async (_, _) => { await Task.Delay(4000); if (WidgetRules.Parse(File.ReadAllText(reviewFile), out _) is { } d) { using var f = new WidgetReviewForm(d, install: true); f.ShowDialog(this); } };
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_IMPORT") is { Length: > 0 } importFolder)
            Shown += async (_, _) =>
            {
                await Task.Delay(3000);
                using var f = new ImportServersForm(_core);
                f.LoadRustAdmin(importFolder);
                if (Environment.GetEnvironmentVariable("BUNKER_TEST_IMPORT_SHOT") is { Length: > 0 } picture)
                    f.Shown += async (_, _) => { await Task.Delay(1200); using var bmp = new Bitmap(f.Width, f.Height); f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(picture); f.Close(); };
                f.ShowDialog(this);
            };
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_HIDEFORM") is { Length: > 0 } hideShot)
            Shown += async (_, _) =>
            {
                await Task.Delay(9000);
                using var f = new HiddenMessagesForm(_core);
                f.Shown += async (_, _) => { await Task.Delay(800); using var bmp = new Bitmap(f.Width, f.Height); f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(hideShot); f.Close(); };
                f.ShowDialog(this);
            };
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_PLAYERCARD") is { Length: > 0 } cardShot)
            Shown += async (_, _) =>
            {
                await Task.Delay(12000);
                var first = _core.Monitor.Players.FirstOrDefault();
                if (first is null) return;
                using var f = new PlayerCardForm(_core, new PlayerTarget(first.SteamId, first.Name, first.Ping, true)) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 100) };
                f.Shown += async (_, _) => { await Task.Delay(1500); using var bmp = new Bitmap(f.Width, f.Height); f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(cardShot); f.Close(); };
                f.ShowDialog(this);
            };
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_SHARE") is { Length: > 0 } shareShot && Environment.GetEnvironmentVariable("BUNKER_TEST_SHARE_FILE") is { Length: > 0 } shareFile)
            Shown += async (_, _) =>
            {
                await Task.Delay(3000);
                using var f = new ShareWidgetForm(_core) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 100) };
                f.Shown += async (_, _) => { f.Load(File.ReadAllText(shareFile), Path.GetFileName(shareFile)); await Task.Delay(800); using var bmp = new Bitmap(f.Width, f.Height); f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(shareShot); f.Close(); };
                f.ShowDialog(this);
            };
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_ONBOARD") is { Length: > 0 } onboardShot)
            Shown += async (_, _) =>
            {
                await Task.Delay(3000);
                using var f = new OnboardingForm(null, firstRun: true) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 100) };
                f.Shown += async (_, _) => { await Task.Delay(800); using var bmp = new Bitmap(f.Width, f.Height); f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(onboardShot); f.Close(); };
                f.ShowDialog(this);
            };
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_WHATSNEW") is { Length: > 0 } notesShot)
            Shown += async (_, _) =>
            {
                await Task.Delay(5000);
                using var f = new WhatsNewForm(WhatsNew.Load().Take(4).ToList(), "Bunker was updated to " + WhatsNew.Load()[0].Version);
                f.Shown += async (_, _) => { await Task.Delay(800); using var bmp = new Bitmap(f.Width, f.Height); f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(notesShot); f.Close(); };
                f.ShowDialog(this);
            };
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_CLOSEFORM") is { Length: > 0 } closeShot)
            Shown += async (_, _) =>
            {
                await Task.Delay(8000);
                BeginInvoke(() => Close()); // the same path as the X: opens the dialog
                await Task.Delay(1500);
                if (OwnedForms.FirstOrDefault(f => f is CloseChoiceForm) is { } f)
                {
                    using var bmp = new Bitmap(f.Width, f.Height); f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(closeShot);
                    f.Close();
                }
            };
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_FULLSCREEN") == "1")
            Shown += async (_, _) => { await Task.Delay(6000); using var f = new FullscreenForm(_core, this); f.ShowDialog(this); };
#endif
        FormClosing += OnFormClosing;
#if DEBUG
        if (Environment.GetEnvironmentVariable("BUNKER_OFFSCREEN") != "1" || Environment.GetEnvironmentVariable("BUNKER_TEST_TRAY") is { Length: > 0 }) // test runs get killed, which would leave a ghost icon in the real tray
#endif
            _tray = new TrayService(this, _core);
#if DEBUG
        // Test hook (debug builds only): show the tray icon for a few seconds, then quit cleanly so no ghost icon is left behind.
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_TRAY") is { Length: > 0 } trayResult)
            Shown += async (_, _) => { await Task.Delay(4000); File.WriteAllText(trayResult, "tray created and running"); Application.Exit(); };
#endif
        FormClosed += (_, _) => _tray?.Dispose();
    }

    /// <summary>The X, Alt+F4 or the taskbar's Close ask whether to minimise instead (unless a choice was remembered). Updates, restarts and Windows shutting down are never asked.</summary>
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            var choice = ChooseCloseAction();
            if (choice == CloseChoice.Cancel) { e.Cancel = true; return; }
            if (choice == CloseChoice.Minimise) { e.Cancel = true; WindowState = FormWindowState.Minimized; return; }
        }
        _core.Rcon.Dispose();
    }

    private CloseChoice ChooseCloseAction()
    {
        switch (CloseButtonSetting.Normalise(_core.Settings.CloseButton))
        {
            case CloseButtonSetting.Minimise: return CloseChoice.Minimise;
            case CloseButtonSetting.Close: return CloseChoice.Close;
        }
        using var form = new CloseChoiceForm();
        form.ShowDialog(this);
        if (form.Choice != CloseChoice.Cancel && form.Remember)
        {
            _core.Settings.CloseButton = form.Choice == CloseChoice.Minimise ? CloseButtonSetting.Minimise : CloseButtonSetting.Close;
            _core.Settings.Save();
        }
        return form.Choice;
    }

    private void Navigate(string key)
    {
        var full = key;
        var section = key.Contains(':') ? key[(key.IndexOf(':') + 1)..] : null;
        if (section is not null) { key = key[..key.IndexOf(':')]; (_pages[key] as ISectionPage)?.ShowSection(section); }
        foreach (var (k, page) in _pages) page.Visible = k == key;
        _sidebar.Select(key == "widgets" ? full : key); // each widget has its own entry in the sidebar
    }

    private async Task StartupAsync()
    {
        _ = RefreshItemsLaterAsync();
        if (await AutoUpdateAsync()) return;
        ShowWhatsNewIfUpdated();
        if (_core.Settings.ActiveServer is null) RunOnboarding();
        if (_core.Settings.ActiveServer is not null && _core.Settings.AutoConnect) await AutoConnectAsync();
    }

    /// <summary>The first time a newer version runs, lists what changed since the version before (once, not on a fresh install).</summary>
    private void ShowWhatsNewIfUpdated()
    {
#if DEBUG
        return; // dev builds never change the real settings or pop notes; BUNKER_TEST_WHATSNEW has its own hook
#endif
        var settings = _core.Settings;
        var current = Updater.CurrentVersion;
        var last = Version.TryParse(settings.LastSeenVersion, out var seen) ? seen : null;
        if (last == current) return;
        var freshInstall = settings.IsFirstRun;
        settings.LastSeenVersion = current.ToString();
        try { settings.Save(); } catch { }
        if (freshInstall) return;

        // A version from before this existed has nothing recorded: tell it about the newest few.
        var notes = WhatsNew.Since(WhatsNew.Load(), last ?? new Version(0, 0, 0), current);
        if (notes.Count == 0) return;
        using var form = new WhatsNewForm(notes, $"Bunker was updated to {current}");
        form.ShowDialog(this);
    }

    /// <summary>A few seconds after launch, quietly checks whether Rust has been updated and fetches the newer item list if so.</summary>
    private async Task RefreshItemsLaterAsync()
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("BUNKER_DATA_DIR") is null) return; // dev builds against real settings stay quiet
#endif
        await Task.Delay(6000);
        await _core.Items.RefreshAsync();
        await _core.WidgetUpdates.CheckAsync(); // at most every 12 hours
    }

    private bool _autoConnectCancelled;

    /// <summary>Waits a few seconds after launch (the network and the window may still be settling), then connects, retrying a couple of times.</summary>
    private async Task AutoConnectAsync()
    {
        const int attempts = 3;
        _core.Log.Add("Connecting in 3 seconds...", LogKind.Info);
        await Task.Delay(3000);
        for (var i = 1; i <= attempts; i++)
        {
            if (_autoConnectCancelled || _core.State != ConnState.Disconnected) return;
            await _core.ConnectAsync();
            if (_core.State == ConnState.Connected || i == attempts) return;
            _core.Log.Add($"Retrying in 5 seconds (attempt {i + 1} of {attempts})...", LogKind.Info);
            await Task.Delay(5000);
        }
    }

    private bool RunOnboarding()
    {
        using var f = new OnboardingForm(null, firstRun: _core.Settings.ActiveServer is null);
        if (f.ShowDialog(this) != DialogResult.OK) return false;
        _core.SaveServer(f.Result!, makeActive: true);

        // "I have more servers to add": ask again, up to the number the Multi Server page can show.
        var more = f.AddAnother;
        while (more && _core.Settings.Servers.Count < ServerGrid.MaxShown)
        {
            using var next = new OnboardingForm(null, firstRun: false, addingAnother: true);
            if (next.ShowDialog(this) != DialogResult.OK) break;
            _core.SaveServer(next.Result!, makeActive: false);
            more = next.AddAnother;
        }
        return true;
    }

    private async Task ToggleConnectionAsync()
    {
        _autoConnectCancelled = true; // the user took over, so stop any automatic retries
        if (_core.State == ConnState.Connected) { await _core.DisconnectAsync(); return; }
        if (_core.Settings.ActiveServer is null && !RunOnboarding()) return;
        await _core.ConnectAsync();
    }

    /// <summary>Returns true if an update was started and the app is closing.</summary>
    private async Task<bool> AutoUpdateAsync()
    {
#if DEBUG
        return false; // dev builds never auto-update (they would close and replace the installed copy)
#endif
        if (Updater.IsStoreVersion || !_core.Settings.CheckForUpdatesOnLaunch) return false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var update = await Updater.CheckAsync(cts.Token);
            if (update is null) return false;
            var answer = MessageBox.Show(this,
                $"Bunker {update.Version} is available (you have {Updater.CurrentVersion}).\n\nUpdate now? Bunker will close and restart.\nChoose No to keep using this version; you'll be asked again next time.",
                "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes)
            {
                _core.Log.Add($"Update {update.Version} is available (skipped for now). You can install it from Settings.", LogKind.Info);
                return false;
            }
            _core.Log.Add($"Downloading update {update.Version}...", LogKind.Warning);
            await Updater.DownloadAndLaunchAsync(update);
            Application.Exit();
            return true;
        }
        catch (Exception e)
        {
            _core.Log.Add("Update check failed: " + e.Message, LogKind.Info);
            return false;
        }
    }
}
