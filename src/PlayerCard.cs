using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Bunker;

/// <summary>
/// A player's card: their Steam profile, how they are doing on your server right now, what Bunker has seen of them before, a note, and the
/// things you can do to them (give an item, kick, ban, mute, teleport). Opened by double-clicking a player.
/// </summary>
public sealed class PlayerCardForm : Form
{
    private readonly Core _core;
    private readonly PlayerTarget _player;
    private readonly CancellationTokenSource _cts = new();

    private readonly PictureBox _avatar = new() { Size = new Size(112, 112), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Input };
    private readonly Label _title = new() { Font = new Font("Segoe UI Semibold", 18f), ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, Height = 36 };
    private readonly Label _subtitle = new() { ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true, Height = 22 };
    private readonly FlowLayoutPanel _badges = new() { AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = new Padding(0) };
    private readonly Dictionary<string, Label> _values = new();
    private readonly Label _steamNote = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(300, 0), Margin = new Padding(0, 6, 0, 0) };
    private readonly TextBox _note = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.UI, Height = 64 };
    private readonly Button _give = Theme.GhostButton("Give item");
    private readonly Button _kick = Theme.GhostButton("Kick");
    private readonly Button _ban = Theme.GhostButton("Ban");
    private readonly Button _mute = Theme.GhostButton("Mute");
    private readonly Button _more = Theme.GhostButton("More   ▾");
    private SteamProfile? _profile;

    public PlayerCardForm(Core core, PlayerTarget player)
    {
        _core = core;
        _player = player;
        Theme.Apply(this);
        Text = "Player card";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(790, 700);

        // Top: picture, name, status badges.
        var head = new Panel { Dock = DockStyle.Top, Height = 168, BackColor = Theme.Panel };
        _avatar.Location = new Point(28, 28);
        _title.SetBounds(160, 28, 600, 36);
        _subtitle.SetBounds(160, 66, 600, 22);
        _badges.Location = new Point(160, 98);
        head.Controls.AddRange(new Control[] { _avatar, _title, _subtitle, _badges });
        head.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, head.Height - 1, head.Width, head.Height - 1); };

        // Middle: two columns of facts.
        var columns = new TableLayoutPanel { Dock = DockStyle.Top, Height = 340, ColumnCount = 2, BackColor = Theme.Bg, Padding = new Padding(28, 14, 28, 0) };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var steam = Section("STEAM", new[] { "Steam ID", "Profile", "Account age", "Rust hours", "Bans", "Account", "Location" });
        steam.Controls.Add(_steamNote);
        columns.Controls.Add(steam, 0, 0);
        columns.Controls.Add(Section("ON YOUR SERVER", new[] { "Status", "Ping", "Health", "This visit", "IP address", "First seen", "Last seen", "Total time", "Visits" }), 1, 0);

        // Note.
        var noteBox = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Theme.Bg, Padding = new Padding(28, 6, 28, 0) };
        noteBox.Controls.Add(_note);
        noteBox.Controls.Add(new Label { Text = "YOUR NOTE (saved on this PC, shown in the Player database)", Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Muted, Font = new Font("Segoe UI Semibold", 8f) });
        _note.Dock = DockStyle.Fill;
        DarkScroll.Apply(_note); // the note box scrolls with the same dark bar as every other box

        // Bottom: the things you can do.
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 70, BackColor = Theme.Panel, Padding = new Padding(28, 16, 28, 0), WrapContents = false };
        bar.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
        var close = Theme.GhostButton("Close");
        foreach (var b in new[] { _give, _kick, _ban, _mute, _more, close }) { b.Height = 38; b.Margin = new Padding(0, 0, 8, 0); bar.Controls.Add(b); }
        _ban.ForeColor = Theme.Err;

        Controls.Add(noteBox);
        Controls.Add(columns);
        Controls.Add(head);
        Controls.Add(bar);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });
        CancelButton = close;
        close.Click += (_, _) => Close();

        _give.Click += async (_, _) => await PlayerMenu.GiveItem(_core, this, _player);
        _kick.Click += async (_, _) => await PlayerMenu.Moderate(_core, this, _player, "kick");
        _ban.Click += async (_, _) => await PlayerMenu.Moderate(_core, this, _player, "ban");
        _mute.Click += async (_, _) => await PlayerMenu.Moderate(_core, this, _player, "mute");
        _more.Click += (_, _) => ShowMore();
        _note.Leave += (_, _) => SaveNote();
        FormClosing += (_, _) => SaveNote();

        _core.Monitor.Updated += RefreshServerSide;
        _core.Tracker.Changed += RefreshServerSide;
        _core.StateChanged += RefreshServerSide;
        FormClosed += (_, _) =>
        {
            _core.Monitor.Updated -= RefreshServerSide;
            _core.Tracker.Changed -= RefreshServerSide;
            _core.StateChanged -= RefreshServerSide;
            _cts.Cancel();
            _cts.Dispose();
        };

        Tips.Set(_give, "Give this player an item");
        Tips.Set(_more, "Teleport, unmute, copy, open the Steam profile and more");
        _title.Text = player.Name;
        _avatar.Image = Placeholder(player.Name);
        _note.Text = Record()?.Note ?? "";
        Shown += (_, _) => { _note.SelectionStart = _note.TextLength; _note.SelectionLength = 0; ActiveControl = null; }; // not all selected, as if about to be typed over
        FillSteam(null, "Looking up the Steam profile...");
        RefreshServerSide();
        Shown += async (_, _) => await LoadSteamAsync();
    }

    // ---------------------------------------------------------------- layout helpers

    private Control Section(string heading, string[] rows)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0) };
        flow.Controls.Add(new Label { Text = heading, ForeColor = Theme.Accent, Font = new Font("Segoe UI Semibold", 8f), AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
        foreach (var key in rows)
        {
            var row = new Panel { Width = 340, Height = 30, BackColor = Theme.Bg, Margin = new Padding(0) };
            row.Controls.Add(new Label { Text = key, ForeColor = Theme.Muted, Font = Theme.Small, Location = new Point(0, 7), Size = new Size(96, 18) });
            var value = new Label { ForeColor = Theme.Text, Location = new Point(100, 6), Size = new Size(240, 20), AutoEllipsis = true, Text = "-" };
            row.Controls.Add(value);
            flow.Controls.Add(row);
            _values[heading + "/" + key] = value;
        }
        return flow;
    }

    private void Set(string section, string key, string text, Color? colour = null)
    {
        if (!_values.TryGetValue(section + "/" + key, out var label)) return;
        label.Text = text;
        label.ForeColor = colour ?? Theme.Text;
        Tips.Set(label, text);
    }

    private void Badge(string text, Color colour)
    {
        var pill = new Label
        {
            Text = text, AutoSize = true, ForeColor = colour, Font = new Font("Segoe UI Semibold", 8.5f), Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 8, 0),
            BackColor = Color.FromArgb(Theme.IsLight ? 28 : 46, colour),
        };
        _badges.Controls.Add(pill);
    }

    /// <summary>A round-cornered square with the player's initial, shown until (or instead of) their Steam picture.</summary>
    private static Bitmap Placeholder(string name)
    {
        var bmp = new Bitmap(112, 112);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Input);
        var letter = name.Trim().Length > 0 ? char.ToUpperInvariant(name.Trim()[0]).ToString() : "?";
        TextRenderer.DrawText(g, letter, new Font("Segoe UI Semibold", 38f), new Rectangle(0, 0, 112, 112), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return bmp;
    }

    // ---------------------------------------------------------------- what is known now

    private PlayerRecord? Record() => _core.Tracker.Records.FirstOrDefault(r => r.SteamId == _player.SteamId);
    private PlayerInfo? Live() => _core.Monitor.Players.FirstOrDefault(p => p.SteamId == _player.SteamId);

    /// <summary>The parts that change while the card is open: whether they are on the server, ping, health, time, and which buttons work.</summary>
    private void RefreshServerSide()
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke(RefreshServerSide); return; }
        const string S = "ON YOUR SERVER";
        var live = Live();
        var record = Record();
        var connected = _core.State == ConnState.Connected;
        var online = connected && live is not null;
        var safeId = _player.SteamId.All(char.IsDigit);

        if (live is not null)
        {
            Set(S, "Status", "Online now", Theme.Ok);
            Set(S, "Ping", live.Ping + " ms");
            Set(S, "Health", Math.Round(live.Health) + " HP");
            Set(S, "This visit", PlayersView.Duration(live.ConnectedSeconds));
            Set(S, "IP address", StripPort(live.Address) is { Length: > 0 } ip ? ip : "-");
        }
        else
        {
            Set(S, "Status", connected ? "Not on the server" : "Not connected to the server", Theme.Muted);
            foreach (var k in new[] { "Ping", "Health", "This visit", "IP address" }) Set(S, k, "-", Theme.Muted);
        }
        Set(S, "First seen", record is null ? "-" : record.FirstSeenUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm"));
        Set(S, "Last seen", record is null ? "-" : live is not null ? "Now" : record.LastSeenUtc.ToLocalTime().ToString("d MMM yyyy, HH:mm"));
        Set(S, "Total time", record is null ? "-" : PlayersView.Duration((long)record.TotalSeconds));
        Set(S, "Visits", record?.Sessions.ToString() ?? "-");

        // The name is the one they use on your server; if their Steam name is different it is shown under it.
        _title.Text = live?.Name ?? _player.Name;
        _subtitle.Text = _profile is { } sp && !string.Equals(sp.PersonaName, _title.Text, StringComparison.CurrentCultureIgnoreCase)
            ? $"Steam name: {sp.PersonaName}" : _player.SteamId;

        _give.Enabled = _kick.Enabled = _mute.Enabled = online && safeId;
        _ban.Enabled = connected && safeId; // a ban works for players who have left too
        ShowBadges(live is not null);
    }

    private void ShowBadges(bool online)
    {
        _badges.SuspendLayout();
        foreach (Control c in _badges.Controls.Cast<Control>().ToList()) { _badges.Controls.Remove(c); c.Dispose(); }
        Badge(online ? "ONLINE" : "NOT ON THE SERVER", online ? Theme.Ok : Theme.Muted);
        if (_profile is { } p)
        {
            var (text, bad) = SteamLookup.Bans(p);
            if (bad) Badge(text.ToUpperInvariant(), Theme.Err);
            if (!p.IsPublic) Badge($"STEAM PROFILE: {SteamLookup.PrivacyText(p).ToUpperInvariant()}", Theme.Warn);
            if (p.Limited) Badge("LIMITED ACCOUNT", Theme.Warn);
        }
        if (Record() is { Note.Length: > 0 }) Badge("HAS A NOTE", Theme.Chat);
        _badges.ResumeLayout();
    }

    private static string StripPort(string address)
    {
        var a = address.Trim();
        var colon = a.LastIndexOf(':');
        return colon > 0 && a.IndexOf(':') == colon ? a[..colon] : a; // an address with one colon is ip:port (leave IPv6 alone)
    }

    // ---------------------------------------------------------------- Steam

    private async Task LoadSteamAsync()
    {
        if (!_core.Settings.SteamLookups) { FillSteam(null, "Steam lookups are switched off in Settings → General."); return; }
        if (!SteamLookup.IsSteamId(_player.SteamId)) { FillSteam(null, "This player has no Steam ID to look up."); return; }
        try
        {
            var result = await SteamLookup.GetAsync(_player.SteamId, _core.Settings.SteamApiKey, _cts.Token);
            if (IsDisposed) return;
            _profile = result.Profile;
            if (result.Avatar is { Length: > 0 } bytes)
            {
                try { using var ms = new MemoryStream(bytes); _avatar.Image = new Bitmap(ms); } catch { /* keep the initial */ }
            }
            FillSteam(result.Profile, result.Message);
            RefreshServerSide();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) FillSteam(null, "Could not look up Steam: " + e.Message); }
    }

    private void FillSteam(SteamProfile? p, string message)
    {
        const string S = "STEAM";
        Set(S, "Steam ID", _player.SteamId);
        if (p is null)
        {
            foreach (var key in new[] { "Profile", "Account age", "Rust hours", "Bans", "Account", "Location" }) Set(S, key, "-", Theme.Muted);
            _steamNote.Text = message;
            return;
        }
        var haveKey = SteamLookup.LooksLikeKey(_core.Settings.SteamApiKey);
        Set(S, "Profile", SteamLookup.PrivacyText(p), p.IsPublic ? Theme.Text : Theme.Warn);
        var age = SteamLookup.AccountAge(p);
        Set(S, "Account age", age, p.AccountCreated is null && p.MemberSince is null ? Theme.Muted : null);
        Set(S, "Rust hours", SteamLookup.RustLine(p, haveKey), p.RustHours is null ? Theme.Muted : null);
        var (bans, bad) = SteamLookup.Bans(p);
        Set(S, "Bans", bans, bad ? Theme.Err : Theme.Ok);
        Set(S, "Account", p.Limited ? "Limited (new or unpaid)" : "Normal");
        Set(S, "Location", p.Location ?? p.CountryCode ?? "Hidden", p.Location is null && p.CountryCode is null ? Theme.Muted : null);
        var source = p.HasKeyData ? "From the player's Steam profile and the Steam Web API." : p.IsPublic ? "From the player's public Steam profile." : "This profile is not public, so Steam shares very little.";
        _steamNote.Text = message.Length > 0 ? message : source;
    }

    // ---------------------------------------------------------------- actions

    private void SaveNote()
    {
        var existing = Record()?.Note ?? "";
        var text = _note.Text.Trim();
        if (text != existing && (Record() is not null || text.Length > 0)) _core.Tracker.SetNote(_player.SteamId, text);
    }

    private void ShowMore()
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        PlayerMenu.Rebuild(menu, _core, _player, this);
        menu.Show(_more, new Point(0, _more.Height));
    }

    /// <summary>Opens a player's card over the window that asked.</summary>
    public static void Open(Core core, PlayerTarget player, IWin32Window? owner)
    {
        using var card = new PlayerCardForm(core, player);
        card.ShowDialog(owner);
    }
}
