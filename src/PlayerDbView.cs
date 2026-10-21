using System.Diagnostics;
using System.Drawing;

namespace Bunker;

/// <summary>Everyone who has been on the server: when they were first and last seen, time played, sessions, and your own notes.</summary>
public class PlayerDbView : Panel
{
    private readonly Core _core;
    private readonly DarkGrid _grid = new();
    private readonly ModernField _search = new();
    private readonly CheckBox _onlineOnly = new() { Text = "Online now", AutoSize = true, ForeColor = Theme.Text, Cursor = Cursors.Hand, Margin = new Padding(14, 10, 0, 0) };
    private readonly Button _export = Theme.GhostButton("Export CSV...");
    private List<PlayerRecord> _shown = new();
    private readonly Label _count = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(14, 12, 0, 0) };

    private readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 12f), ForeColor = Theme.Text };
    private readonly Label _facts = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, MaximumSize = new Size(420, 0) };
    private readonly TextBox _note = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.UI,
    };
    private readonly ModernField _reason = new();
    private readonly Button _copy = Theme.GhostButton("Copy ID");
    private readonly Button _profile = Theme.GhostButton("Steam profile");
    private readonly Button _kick = Theme.GhostButton("Kick");
    private readonly Button _ban = Theme.GhostButton("Ban");
    private readonly Button _mute = Theme.GhostButton("Mute");
    private readonly Button _saveNote = Theme.PrimaryButton("Save note");

    private string? _selectedId;
    private bool _filling;

    public PlayerDbView(Core core)
    {
        _core = core;
        BackColor = Theme.Panel;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(0, 6, 0, 0), BackColor = Theme.Panel, WrapContents = false };
        _search.Width = 320;
        _search.Box.PlaceholderText = "Search by name, Steam ID or note";
        top.Controls.Add(_search);
        top.Controls.Add(_onlineOnly);
        top.Controls.Add(_count);
        _export.Height = 34;
        _export.Margin = new Padding(14, 1, 0, 0);
        Tips.Set(_export, "Save the players shown here (with the search applied) as a spreadsheet file");
        top.Controls.Add(_export);
        _export.Click += (_, _) => ExportCsv();

        foreach (var (key, title, weight) in new[] { ("Status", "Status", 70), ("Name", "Name", 120), ("Id", "Steam ID", 130), ("First", "First seen", 90), ("Time", "Time played", 80), ("Sessions", "Visits", 45), ("Note", "Note", 160) })
        {
            _grid.Columns.Add(key, title);
            _grid.Columns[key].FillWeight = weight;
        }
        _grid.Dock = DockStyle.Fill;

        // Detail panel for the selected player
        var detail = new Panel { Dock = DockStyle.Bottom, Height = 214, Padding = new Padding(0, 12, 0, 0), BackColor = Theme.Panel };
        detail.Paint += (_, e) => { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 0, 0, detail.Width, 0); };

        var left = new Panel { Dock = DockStyle.Left, Width = 460, BackColor = Theme.Panel, Padding = new Padding(0, 4, 12, 0) };
        _title.Location = new Point(0, 6);
        _facts.Location = new Point(0, 34);
        _reason.Location = new Point(0, 116); _reason.Width = 430; _reason.Box.PlaceholderText = "Reason for kick or ban";
        var actions = new FlowLayoutPanel { Location = new Point(0, 160), Size = new Size(450, 44), BackColor = Theme.Panel, WrapContents = false };
        foreach (var b in new[] { _copy, _profile, _kick, _ban, _mute }) { b.Height = 34; b.Width = Math.Max(76, b.Width - 22); b.Margin = new Padding(0, 0, 6, 0); actions.Controls.Add(b); }
        left.Controls.AddRange(new Control[] { _title, _facts, _reason, actions });

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };
        var noteCaption = new Label { Text = "Your notes about this player (saved on this PC)", ForeColor = Theme.Muted, Font = Theme.Small, Dock = DockStyle.Top, Height = 22 };
        _saveNote.Dock = DockStyle.Bottom; _saveNote.Height = 36;
        _note.Dock = DockStyle.Fill;
        right.Controls.Add(_note);
        right.Controls.Add(_saveNote);
        right.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 8, BackColor = Theme.Panel });
        right.Controls.Add(noteCaption);

        detail.Controls.Add(right);
        detail.Controls.Add(left);

        Controls.Add(_grid);
        Controls.Add(detail);
        Controls.Add(top);
        DarkScroll.Apply(_note);

        _search.Box.TextChanged += (_, _) => Fill();
        _onlineOnly.CheckedChanged += (_, _) => Fill();
        _grid.SelectionChanged += (_, _) => OnSelected();
        // The same right-click menu as the Players page. Players who have left can still be banned or given a note.
        PlayerMenu.Attach(_grid, _core, () => Selected() is { } r ? new PlayerTarget(r.SteamId, r.Name, null, _core.Tracker.IsOnline(r.SteamId)) : null);
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0) { _grid.ClearSelection(); _grid.Rows[e.RowIndex].Selected = true; }
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].Tag is PlayerRecord r) PlayerCardForm.Open(_core, new PlayerTarget(r.SteamId, r.Name, null, _core.Tracker.IsOnline(r.SteamId)), FindForm());
        };
        _saveNote.Click += (_, _) => SaveNote();
        _note.Leave += (_, _) => SaveNote();
        _copy.Click += (_, _) => { if (_selectedId is not null) Clipboard.SetText(_selectedId); };
        _profile.Click += (_, _) => { if (_selectedId is not null) try { Process.Start(new ProcessStartInfo($"https://steamcommunity.com/profiles/{_selectedId}") { UseShellExecute = true }); } catch { } };
        _kick.Click += async (_, _) => await ActAsync("kick");
        _ban.Click += async (_, _) => await ActAsync("ban");
        _mute.Click += async (_, _) => await ActAsync("mute");
        _core.Tracker.Changed += Fill;
        _core.StateChanged += UpdateButtons;
        Fill();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _core.Tracker.Changed -= Fill; _core.StateChanged -= UpdateButtons; }
        base.Dispose(disposing);
    }

    /// <summary>Saves the players shown (search applied) as a CSV file that opens in Excel.</summary>
    private void ExportCsv()
    {
        if (_shown.Count == 0) { MessageBox.Show(FindForm(), "There are no players to save yet.", "Export players", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var server = _core.Settings.ActiveServer?.DisplayName ?? "server";
        using var dialog = new SaveFileDialog
        {
            Title = "Export players", Filter = "CSV file (*.csv)|*.csv", OverwritePrompt = true,
            InitialDirectory = ExportText.EnsureFolder(),
            FileName = ExportText.FileName("players-" + server, DateTime.Now, "csv"),
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, ExportText.PlayersCsv(_shown, _core.Tracker.IsOnline), new System.Text.UTF8Encoding(true));
            _core.Log.Add($"Saved {_shown.Count:N0} player{(_shown.Count == 1 ? "" : "s")} to {dialog.FileName}", LogKind.Info);
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), "Could not save the file: " + ex.Message, "Export players", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private PlayerRecord? Selected() => _selectedId is null ? null : _core.Tracker.Records.FirstOrDefault(r => r.SteamId == _selectedId);

    private static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes}m ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours}h ago" : $"{(int)d.TotalDays}d ago";
    }

    private void Fill()
    {
        _filling = true;
        var q = _search.Text.Trim();
        var rows = _core.Tracker.Records
            .Where(r => !_onlineOnly.Checked || _core.Tracker.IsOnline(r.SteamId))
            .Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.SteamId.Contains(q) || r.Note.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => _core.Tracker.IsOnline(r.SteamId)).ThenByDescending(r => r.LastSeenUtc).ToList();
        _shown = rows;

        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var r in rows)
        {
            var online = _core.Tracker.IsOnline(r.SteamId);
            var i = _grid.Rows.Add(online ? "Online" : "Left " + Ago(r.LastSeenUtc), r.Name, r.SteamId, r.FirstSeenUtc.ToLocalTime().ToString("d MMM yyyy"),
                PlayersView.Duration((long)r.TotalSeconds), r.Sessions, r.Note.Replace('\n', ' '));
            _grid.Rows[i].Tag = r;
            _grid.Rows[i].Cells[0].Style.ForeColor = online ? Theme.Ok : Theme.Muted;
            if (r.SteamId == _selectedId) _grid.Rows[i].Selected = true;
        }
        _grid.ResumeLayout();
        _count.Text = $"{rows.Count} shown  -  {_core.Tracker.Records.Count} known  -  {_core.Tracker.Records.Count(r => _core.Tracker.IsOnline(r.SteamId))} online";
        _filling = false;

        // Keep a player selected: the previous one if still listed, otherwise the first row.
        var stillListed = rows.Any(r => r.SteamId == _selectedId);
        if (!stillListed)
        {
            _selectedId = rows.FirstOrDefault()?.SteamId;
            if (_grid.Rows.Count > 0) { _filling = true; _grid.ClearSelection(); _grid.Rows[0].Selected = true; _filling = false; }
            UpdateDetail(true);
        }
        else UpdateDetail(false);
    }

    private void OnSelected()
    {
        if (_filling) return;
        var id = (_grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as PlayerRecord : null)?.SteamId;
        if (id == _selectedId) return;
        SaveNote();
        _selectedId = id;
        UpdateDetail(true);
    }

    private void UpdateDetail(bool replaceNote)
    {
        var r = Selected();
        if (r is null)
        {
            _title.Text = "No player selected";
            _facts.Text = "Players appear here once they have been seen on the server.";
            if (replaceNote) _note.Text = "";
            _note.Enabled = _saveNote.Enabled = false;
            UpdateButtons();
            return;
        }
        var online = _core.Tracker.IsOnline(r.SteamId);
        _title.Text = r.Name;
        _facts.Text = $"{r.SteamId}\n{(online ? "Online now" : "Last seen " + r.LastSeenUtc.ToLocalTime().ToString("d MMM yyyy HH:mm"))}  -  first seen {r.FirstSeenUtc.ToLocalTime():d MMM yyyy}\n" +
                      $"{PlayersView.Duration((long)r.TotalSeconds)} played in {r.Sessions} visit{(r.Sessions == 1 ? "" : "s")}";
        if (replaceNote || !_note.Focused) _note.Text = r.Note;
        _note.Enabled = _saveNote.Enabled = true;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var has = Selected() is not null;
        var connected = _core.State == ConnState.Connected;
        _copy.Enabled = _profile.Enabled = has;
        _kick.Enabled = _ban.Enabled = _mute.Enabled = has && connected;
    }

    private void SaveNote()
    {
        if (_selectedId is not null && _note.Enabled) _core.Tracker.SetNote(_selectedId, _note.Text.Trim());
    }

    private async Task ActAsync(string verb)
    {
        if (Selected() is not { } r) return;
        var reason = _reason.Text.Trim();
        if (reason.Length == 0) reason = verb == "ban" ? "Banned by admin" : "Kicked by admin";
        var title = verb == "ban" ? "Ban player" : verb == "mute" ? "Mute player" : "Kick player";
        var text = verb == "mute" ? $"Mute {r.Name}?" : $"{(verb == "ban" ? "Ban" : "Kick")} {r.Name}?\n\nReason: {reason}";
        if (MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        switch (verb)
        {
            case "ban":
                await _core.SendCommandAsync($"banid {r.SteamId} {Cmd.Quote(r.Name)} {Cmd.Quote(reason)}");
                await _core.SendCommandAsync("server.writecfg");
                break;
            case "mute":
                await _core.SendCommandAsync($"mute \"{r.SteamId}\"");
                break;
            default:
                await _core.SendCommandAsync($"kick \"{r.SteamId}\" {Cmd.Quote(reason)}");
                break;
        }
    }
}

/// <summary>A live feed of who joined and who left, newest first.</summary>
public class ActivityView : Panel
{
    private readonly Core _core;
    private readonly DarkGrid _grid = new();

    public ActivityView(Core core)
    {
        _core = core;
        BackColor = Theme.Panel;
        foreach (var (key, title, weight) in new[] { ("Time", "Time", 60), ("Event", "Event", 60), ("Player", "Player", 140), ("Id", "Steam ID", 130) })
        {
            _grid.Columns.Add(key, title);
            _grid.Columns[key].FillWeight = weight;
        }
        _grid.Dock = DockStyle.Fill;
        Controls.Add(_grid);

        var hint = new Label
        {
            Text = "Joins and leaves are noticed from the player list every few seconds, starting from when Bunker connected.",
            Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(0, 4, 0, 0),
        };
        Controls.Add(hint);

        foreach (var e in _core.Tracker.Events) Add(e);
        _core.Tracker.ActivityAdded += OnAdded;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _core.Tracker.ActivityAdded -= OnAdded;
        base.Dispose(disposing);
    }

    private void OnAdded(ActivityEvent e) => Add(e);

    private void Add(ActivityEvent e)
    {
        // newest first
        _grid.Rows.Insert(0, e.Time.ToString("HH:mm:ss"), e.Joined ? "Joined" : "Left", e.Name, e.SteamId);
        _grid.Rows[0].Cells[1].Style.ForeColor = e.Joined ? Theme.Ok : Theme.Muted;
        while (_grid.Rows.Count > 300) _grid.Rows.RemoveAt(_grid.Rows.Count - 1);
    }
}
