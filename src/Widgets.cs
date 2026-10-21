using System.Drawing;

namespace Bunker;

/// <summary>Console view (all output + command box) or Chat view (chat only, input sends "say").</summary>
public class ConsoleView : Panel
{
    private readonly Core _core;
    private readonly bool _chat;
    private readonly bool _showInput;
    private readonly RichTextBox _box = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Bg,
        ForeColor = Theme.Text, Font = Theme.Mono, HideSelection = false, DetectUrls = false,
    };
    private readonly ModernField _input = new();
    private readonly Button _send = Theme.PrimaryButton("Send");
    private readonly Button _mode = Theme.GhostButton("Command   ▾");
    private bool _sayInChat; // console only: what you type is said in the chat instead of run as a command
    private const string CommandPlaceholder = "Type a command (e.g. status, say hello)";
    private readonly CheckBox _autoScroll = new() { Text = "Auto-scroll", Checked = true, AutoSize = true, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Margin = new Padding(0, 3, 12, 0) };
    private readonly Label _save = new() { Text = "Save", AutoSize = true, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Margin = new Padding(0, 4, 10, 0) };
    private readonly Label _clear = new() { Text = "Clear", AutoSize = true, ForeColor = Theme.Muted, Cursor = Cursors.Hand, Margin = new Padding(0, 4, 4, 0) };
    private readonly List<string> _history = new();
    private Panel? _bar;
    private int _historyIndex;

    // Search and filters (console mode only)
    private readonly ModernField _search = new();
    private bool _showChat = true, _showWarnings = true, _showErrors = true, _showCommands = true;
    private bool _showHiddenNow; // reveals lines hidden by your rules, until you turn it off

    /// <summary>Controls the hosting Card should show on the right of its header.</summary>
    public Control[] HeaderActions => new Control[] { _autoScroll, _save, _clear };

    public ConsoleView(Core core, bool chat = false, bool showInput = true)
    {
        _core = core;
        _chat = chat;
        _showInput = showInput;
        BackColor = Theme.Panel;

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(10, 8, 6, 6) };
        host.Controls.Add(_box);
        Controls.Add(host);

        if (!chat) Controls.Add(BuildFilterBar());

        if (showInput)
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 10, 0, 0), BackColor = Theme.Panel };
            _input.Dock = DockStyle.Fill;
            _input.Box.Font = Theme.Mono;
            _input.Box.PlaceholderText = chat ? ChatPlaceholder() : CommandPlaceholder;
            var gap = new Panel { Dock = DockStyle.Right, Width = 8 };
            _send.Dock = DockStyle.Right;
            _send.Width = 86;
            _send.Height = 36;
            bar.Controls.AddRange(new Control[] { _input, gap, _send });
            if (!chat)
            {
                // A choice between running what you type as a command, or saying it in the game's chat as you.
                _mode.Dock = DockStyle.Left;
                _mode.Width = 132;
                _mode.Height = 36;
                _mode.Click += (_, _) => ShowModeMenu();
                Tips.Set(_mode, "Choose whether what you type is run as a command, or said in the game's chat as you.");
                bar.Controls.Add(_mode);
                bar.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 8 });
            }
            Controls.Add(bar);
            _bar = bar;
            _send.Click += async (_, _) => await SendAsync();
            _input.Box.KeyDown += InputKeyDown;
            ApplyEnabled();
        }

        DarkScroll.Apply(_box);
        ApplyFont();
        _core.ConsoleFontChanged += ApplyFont;
        if (chat && showInput) _core.ChatIdentityChanged += RefreshChatPlaceholder;
        foreach (var e in _core.Log.Entries) Append(e);
        _core.Log.Added += OnAdded;
        _core.Log.Cleared += OnCleared;
        if (!chat)
        {
            _core.ConsoleFilterChanged += RebuildView;
            _box.ContextMenuStrip = new ContextMenuStrip(); // an empty one, so the built-in text-box menu does not appear next to ours
            _box.MouseDown += OnBoxMouseDown;
        }
        _core.StateChanged += ApplyEnabled;
        _save.Click += (_, _) => SaveLog();
        _save.MouseEnter += (_, _) => _save.ForeColor = Theme.Accent;
        _save.MouseLeave += (_, _) => _save.ForeColor = Theme.Muted;
        Tips.Set(_save, "Save what is shown here (with the search and filters applied) to a text file");
        _clear.Click += (_, _) => _core.Log.Clear();
        _clear.MouseEnter += (_, _) => _clear.ForeColor = Theme.Accent;
        _clear.MouseLeave += (_, _) => _clear.ForeColor = Theme.Muted;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _core.Log.Added -= OnAdded;
            _core.Log.Cleared -= OnCleared;
            _core.ConsoleFilterChanged -= RebuildView;
            _core.StateChanged -= ApplyEnabled;
            _core.ConsoleFontChanged -= ApplyFont;
            _core.ChatIdentityChanged -= RefreshChatPlaceholder;
        }
        base.Dispose(disposing);
    }

    private string ChatPlaceholder() =>
        _core.Settings.ChatName.Trim() is { Length: > 0 } name ? $"Say something as {name}..." : "Say something to the server (as SERVER; set your name in Settings → Chat)";

    /// <summary>Uses the font chosen in Settings, and makes the command bar tall enough for it.</summary>
    private void ApplyFont()
    {
        _box.Font = _core.ConsoleFont;
        if (!_showInput) return;
        _input.Box.Font = _core.ConsoleFont;
        if (_bar is not null) _bar.Height = Math.Max(52, _input.Box.PreferredHeight + 42);
    }

    private void RefreshChatPlaceholder() => _input.Box.PlaceholderText = _chat || _sayInChat ? ChatPlaceholder() : CommandPlaceholder;

    private void ShowModeMenu()
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
        var name = _core.Settings.ChatName.Trim();
        void Choose(string label, bool chat)
        {
            var item = new ToolStripMenuItem(label) { Checked = _sayInChat == chat, BackColor = Theme.Input, ForeColor = Theme.Text };
            item.Click += (_, _) =>
            {
                _sayInChat = chat;
                _mode.Text = chat ? "Chat   ▾" : "Command   ▾";
                RefreshChatPlaceholder();
                _input.Box.Focus();
            };
            menu.Items.Add(item);
        }
        Choose("Command  (run what I type)", false);
        Choose(name.Length > 0 ? $"Chat  (say it as {name})" : "Chat  (say it as SERVER)", true);
        menu.Show(_mode, new Point(0, _mode.Height));
    }

    /// <summary>Saves the lines the console is showing (search and filters applied) to a text file.</summary>
    private void SaveLog()
    {
        var entries = _core.Log.Entries.Where(Passes).ToList();
        if (entries.Count == 0) { MessageBox.Show(FindForm(), "There is nothing to save yet.", "Save the console", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dialog = new SaveFileDialog
        {
            Title = _chat ? "Save the chat" : "Save the console", Filter = "Text file (*.txt)|*.txt", OverwritePrompt = true,
            InitialDirectory = ExportText.EnsureFolder(),
            FileName = ExportText.FileName(_chat ? "bunker-chat" : "bunker-console", DateTime.Now, "txt"),
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, ExportText.LogText(entries), new System.Text.UTF8Encoding(true));
            _core.Log.Add($"Saved {entries.Count:N0} line{(entries.Count == 1 ? "" : "s")} to {dialog.FileName}", LogKind.Info);
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), "Could not save the file: " + ex.Message, "Save the console", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void OnAdded(LogEntry e) => Append(e);
    private void OnCleared() => _box.Clear();

    /// <summary>A search box and a Filter menu that can hide chat, warnings, errors or Bunker's own command lines.</summary>
    private Panel BuildFilterBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Panel, Padding = new Padding(0, 0, 0, 8) };

        var filter = Theme.GhostButton("Filter   ▾");
        filter.Dock = DockStyle.Right;
        filter.Width = 150;
        filter.Height = 36;
        var gap = new Panel { Dock = DockStyle.Right, Width = 8, BackColor = Theme.Panel };

        void UpdateLabel()
        {
            var hidden = new[] { _showChat, _showWarnings, _showErrors, _showCommands }.Count(on => !on) + (_showHiddenNow ? 0 : _core.Settings.ConsoleHidden.Count);
            filter.Text = hidden == 0 ? "Filter   ▾" : $"Filter ({hidden} hidden)  ▾";
        }

        _core.ConsoleFilterChanged += UpdateLabel;
        filter.Disposed += (_, _) => _core.ConsoleFilterChanged -= UpdateLabel;
        UpdateLabel();

        filter.Click += (_, _) =>
        {
            var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
            void Toggle(string text, Func<bool> get, Action<bool> set)
            {
                var item = new ToolStripMenuItem(text) { Checked = get(), CheckOnClick = true, BackColor = Theme.Input, ForeColor = Theme.Text };
                item.Click += (_, _) => { set(item.Checked); UpdateLabel(); RebuildView(); };
                menu.Items.Add(item);
            }
            Toggle("Show chat", () => _showChat, v => _showChat = v);
            Toggle("Show warnings", () => _showWarnings, v => _showWarnings = v);
            Toggle("Show errors", () => _showErrors, v => _showErrors = v);
            Toggle("Show command lines", () => _showCommands, v => _showCommands = v);
            menu.Items.Add(new ToolStripSeparator());
            var count = _core.Settings.ConsoleHidden.Count;
            var manage = new ToolStripMenuItem(count == 0 ? "Hide a plugin or words..." : $"Hide a plugin or words ({count} hidden)...") { BackColor = Theme.Input, ForeColor = Theme.Text };
            manage.Click += (_, _) => { menu.Close(); ManageHidden(); UpdateLabel(); };
            menu.Items.Add(manage);
            if (count > 0)
            {
                var reveal = new ToolStripMenuItem("Show hidden messages for now") { Checked = _showHiddenNow, CheckOnClick = true, BackColor = Theme.Input, ForeColor = Theme.Text };
                reveal.Click += (_, _) => { _showHiddenNow = reveal.Checked; UpdateLabel(); RebuildView(); };
                menu.Items.Add(reveal);
            }
            // Keep the menu open while several items are toggled.
            menu.Closing += (_, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; };
            menu.Show(filter, new Point(filter.Width - menu.PreferredSize.Width, filter.Height));
        };

        _search.Dock = DockStyle.Fill;
        _search.Box.PlaceholderText = "Search the console";
        _search.Box.TextChanged += (_, _) => RebuildView();
#if DEBUG
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_CONSOLE_SEARCH") is { Length: > 0 } testQuery) _search.Text = testQuery;
#endif
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(0, 3, 0, 0) };
        host.Controls.Add(_search);
        bar.Controls.Add(host);
        bar.Controls.Add(gap);
        bar.Controls.Add(filter);
        return bar;
    }

    private bool Passes(LogEntry e)
    {
        if (_chat) return e.Kind == LogKind.Chat;
        if (!_showHiddenNow && ConsoleFilter.IsHidden(_core.Settings.ConsoleHidden, e)) return false;
        if (e.Kind == LogKind.Chat && !_showChat) return false;
        if (e.Kind == LogKind.Warning && !_showWarnings) return false;
        if (e.Kind == LogKind.Error && !_showErrors) return false;
        if (e.Kind is LogKind.Command or LogKind.Info && !_showCommands) return false;
        var q = _search.Text.Trim();
        return q.Length == 0 || e.Text.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- hiding messages

    /// <summary>Right-click on a console line: hide messages like it, or manage the hidden ones.</summary>
    private void OnBoxMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var line = "";
        var index = _box.GetCharIndexFromPosition(e.Location);
        var lineNo = _box.GetLineFromCharIndex(index);
        if (lineNo >= 0 && lineNo < _box.Lines.Length) line = ConsoleFilter.WithoutTime(_box.Lines[lineNo]);

        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        ToolStripMenuItem Item(string text, Action click, bool enabled = true)
        {
            var item = new ToolStripMenuItem(text) { BackColor = Theme.Input, ForeColor = Theme.Text, Enabled = enabled };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }

        if (line.Length > 0)
        {
            if (ConsoleFilter.PluginOf(line) is { } plugin)
                Item($"Hide the plugin [{plugin}]", () => AddHidden(HideRule.PluginKind, plugin));
            Item("Hide messages containing...", () => HideContaining(line));
        }
        Item(_core.Settings.ConsoleHidden.Count == 0 ? "Hide a plugin or words..." : $"Hide a plugin or words ({_core.Settings.ConsoleHidden.Count} hidden)...", ManageHidden);
        menu.Items.Add(new ToolStripSeparator());
        Item("Copy", () => { if (_box.SelectionLength > 0) try { Clipboard.SetText(_box.SelectedText); } catch { } }, _box.SelectionLength > 0);
        menu.Show(_box, e.Location);
    }

    private void AddHidden(string kind, string value)
    {
        if (ConsoleFilter.Add(_core.Settings, kind, value)) _core.NotifyConsoleFilterChanged();
    }

    /// <summary>Asks what to hide, starting from some text (for example the line that was clicked).</summary>
    private void HideContaining(string start)
    {
        var asked = PromptForm.Ask(FindForm(), "Hide messages", "Console lines containing this text will be hidden. You can show them again under Filter → Hide console messages.", "Hide",
            new PromptField("Words to hide", start.Length > ConsoleFilter.MaxLength ? start[..ConsoleFilter.MaxLength] : start));
        if (asked is not null) AddHidden(HideRule.WordsKind, asked[0]);
    }

    private void ManageHidden()
    {
        using var form = new HiddenMessagesForm(_core);
        form.ShowDialog(FindForm());
    }

    /// <summary>Redraws everything that matches the current search and filters.</summary>
    private void RebuildView()
    {
        _box.Clear();
        foreach (var e in _core.Log.Entries) Append(e);
    }

    private void ApplyEnabled()
    {
        if (!_showInput) return;
        var on = _core.State == ConnState.Connected;
        _input.Box.Enabled = on;
        _send.Enabled = on;
    }

    private async Task SendAsync()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0) return;
        _history.Add(text);
        _historyIndex = _history.Count;
        _input.Text = "";
        if (_chat || _sayInChat) await _core.SendChatAsync(text);
        // With a chat name set, "say hello" typed here is said as you (not SERVER), the same as in the Chat panel.
        else if (_core.Settings.ChatName.Trim().Length > 0 && text.StartsWith("say ", StringComparison.OrdinalIgnoreCase) && text.Length > 4) await _core.SendChatAsync(text[4..].Trim());
        else if (!CommandGuess.LooksLikeCommand(text, _confirmedCommands)) await AskCommandOrChatAsync(text);
        else await _core.SendCommandAsync(text);
    }

    private readonly HashSet<string> _confirmedCommands = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What was typed does not look like a server command. Asks whether to say it in the game's chat instead of sending it as one.</summary>
    private async Task AskCommandOrChatAsync(string text)
    {
        var name = _core.Settings.ChatName.Trim();
        var who = name.Length > 0 ? name : "SERVER";
        var shown = text.Length > 60 ? text[..60] + "..." : text;
        var answer = MessageBox.Show(FindForm(),
            $"\"{shown}\" does not look like a server command.\n\nYes: say it in the game's chat as {who}\nNo: run it as a command anyway\nCancel: do not send it",
            "Say this in chat?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes) await _core.SendChatAsync(text);
        else if (answer == DialogResult.No)
        {
            _confirmedCommands.Add(CommandGuess.FirstWord(text)); // it is a command after all (a plugin's, for example): do not ask again
            await _core.SendCommandAsync(text);
        }
        else _input.Text = text; // put it back so nothing is lost
    }

    private void InputKeyDown(object? s, KeyEventArgs e)
    {
        var box = _input.Box;
        if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = SendAsync(); }
        else if (e.KeyCode == Keys.Up && _historyIndex > 0) { box.Text = _history[--_historyIndex]; box.SelectionStart = box.TextLength; e.Handled = true; }
        else if (e.KeyCode == Keys.Down && _historyIndex < _history.Count)
        {
            _historyIndex++;
            box.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
            box.SelectionStart = box.TextLength;
            e.Handled = true;
        }
    }

    internal static Color ColorOf(LogKind k) => k switch
    {
        LogKind.Chat => Theme.Chat,
        LogKind.Warning => Theme.Warn,
        LogKind.Error => Theme.Err,
        LogKind.Command => Theme.Accent,
        LogKind.Info => Theme.Muted,
        _ => Theme.Text,
    };

    private void Append(LogEntry e)
    {
        if (!Passes(e)) return;
        if (_box.TextLength > 400_000) { _box.Select(0, _box.TextLength / 2); _box.SelectedText = ""; }
        _box.SelectionStart = _box.TextLength;
        _box.SelectionLength = 0;
        _box.SelectionColor = Theme.Dim;
        _box.AppendText(e.Time.ToString("HH:mm:ss") + "  ");

        if (e.Kind == LogKind.Chat && e.UserName is { Length: > 0 } who && e.Message is not null)
        {
            // Chat looks the way it does in the game: [TEAM] tag, the player's name in their own colour, then the message in plain text.
            var tint = Theme.Readable(Theme.ParseHex(e.Color) ?? Theme.Chat);
            if (e.Channel == 1) AppendPart("[TEAM] ", Theme.Ok, bold: true);
            // A chat plugin's own prefix, such as [ADMIN]. The server does not send its colour, so it has its own setting.
            if (e.Tags is { Length: > 0 }) AppendPart(e.Tags + " ", Theme.Readable(Theme.ParseHex(_core.Settings.ChatTagColor) ?? Theme.Chat), bold: true);
            AppendPart(who, tint, bold: true);
            AppendPart(": ", Theme.Muted, bold: false);
            AppendPart(e.Message + Environment.NewLine, Theme.Text, bold: false);
        }
        else
        {
            _box.SelectionColor = ColorOf(e.Kind);
            _box.AppendText(e.Text + Environment.NewLine);
        }
        if (_autoScroll.Checked) _box.ScrollToCaret();
    }

    private void AppendPart(string text, Color colour, bool bold)
    {
        _box.SelectionStart = _box.TextLength;
        _box.SelectionLength = 0;
        _box.SelectionColor = colour;
        _box.SelectionFont = bold ? new Font(_box.Font, FontStyle.Bold) : _box.Font;
        _box.AppendText(text);
    }
}

/// <summary>Live player table. Right-click a player for the full menu (see PlayerMenu).</summary>
public class PlayersView : Panel
{
    private readonly Core _core;
    private readonly DataGridView _grid = new DarkGrid();
    private readonly Label _count = new() { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 6, 0, 0) };

    public Control[] HeaderActions => new Control[] { _count };

    public PlayersView(Core core)
    {
        _core = core;
        BackColor = Theme.Panel;

        _grid.Dock = DockStyle.Fill;
        _grid.Columns.Add("Name", "Name");
        _grid.Columns.Add("SteamId", "Steam ID");
        _grid.Columns.Add("Ping", "Ping");
        _grid.Columns.Add("Time", "Time");
        _grid.Columns.Add("Health", "HP");
        _grid.Columns[0].FillWeight = 110;
        _grid.Columns[1].FillWeight = 125;
        _grid.Columns[2].FillWeight = 58;
        _grid.Columns[3].FillWeight = 84;
        _grid.Columns[4].FillWeight = 66;
        Controls.Add(_grid);

        PlayerMenu.Attach(_grid, _core, Selected);
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0) { _grid.ClearSelection(); _grid.Rows[e.RowIndex].Selected = true; }
        };
        // Double-click a player to open their card: Steam profile, stats and everything you can do to them.
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].Tag is PlayerInfo p) PlayerCardForm.Open(_core, new PlayerTarget(p.SteamId, p.Name, p.Ping, true), FindForm());
        };

        _core.Monitor.Updated += Refresh;
        Fill();
#if DEBUG
        // Test hook (debug builds only): open the right-click menu on the first player and save a picture of it.
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_PLAYERMENU") is { Length: > 0 } shot && Environment.GetEnvironmentVariable("BUNKER_START_PAGE") == "players")
            HandleCreated += async (_, _) =>
            {
                try {
                await Task.Delay(8000);
                for (var wait = 0; wait < 60 && _grid.Rows.Count == 0; wait++) await Task.Delay(500);
                if (_grid.Rows.Count == 0) return;
                _grid.Rows[0].Selected = true;
                var menu = _grid.ContextMenuStrip!;
                menu.Show(_grid, new Point(120, 60));
                Application.DoEvents();
                using var bmp = new Bitmap(menu.Width, menu.Height);
                menu.DrawToBitmap(bmp, new Rectangle(0, 0, menu.Width, menu.Height));
                bmp.Save(shot);
                menu.Close();
                } catch (Exception ex) { File.WriteAllText(shot + ".err", ex.ToString()); }
            };
#endif
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _core.Monitor.Updated -= Refresh;
        base.Dispose(disposing);
    }

    private PlayerInfo? Selected() =>
        _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as PlayerInfo : null;

    private new void Refresh() => Fill();

    private void Fill()
    {
        var selectedId = Selected()?.SteamId;
        var players = _core.Monitor.Players.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var p in players)
        {
            var idx = _grid.Rows.Add(p.Name, p.SteamId, p.Ping + "ms", Duration(p.ConnectedSeconds), Math.Round(p.Health).ToString());
            _grid.Rows[idx].Tag = p;
            if (p.SteamId == selectedId) _grid.Rows[idx].Selected = true;
        }
        _grid.ResumeLayout();
        _count.Text = _core.State == ConnState.Connected ? $"{players.Count} online" : "Not connected";
    }

    public static string Duration(long seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
    }
}

/// <summary>A grid of live server stat tiles.</summary>
public class ServerInfoView : Panel
{
    private readonly Core _core;
    private readonly (Label Value, Label Sub)[] _tiles;
    private static readonly string[] Captions = { "PLAYERS", "FPS", "ENTITIES", "UPTIME", "MEMORY", "MAP" };

    public ServerInfoView(Core core, int columns)
    {
        _core = core;
        BackColor = Theme.Panel;
        AutoScroll = true;
        DarkScroll.Apply(this);

        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = columns, BackColor = Theme.Panel };
        for (var c = 0; c < columns; c++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));

        _tiles = new (Label, Label)[Captions.Length];
        for (var i = 0; i < Captions.Length; i++)
        {
            var tile = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Theme.Input, Margin = new Padding(0, 0, 8, 6), Padding = new Padding(14, 10, 8, 6) };
            var value = new Label { Text = "-", Font = new Font("Segoe UI Semibold", 14f), ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, Size = new Size(120, 26), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Location = new Point(12, 21) };
            var sub = new Label { Text = "", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true, Size = new Size(120, 18), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Location = new Point(14, 47) };
            tile.Controls.Add(sub);
            tile.Controls.Add(value);
            tile.Controls.Add(new Label { Text = Captions[i], Font = new Font("Segoe UI Semibold", 8f), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(14, 8) });
            table.Controls.Add(tile, i % columns, i / columns);
            _tiles[i] = (value, sub);
        }
        Controls.Add(table);

        _core.Monitor.Updated += UpdateTiles;
        UpdateTiles();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _core.Monitor.Updated -= UpdateTiles;
        base.Dispose(disposing);
    }

    private void UpdateTiles()
    {
        var i = _core.Monitor.Info;
        if (i is null)
        {
            foreach (var (v, s) in _tiles) { v.Text = "-"; s.Text = ""; }
            return;
        }
        _tiles[0].Value.Text = $"{i.Players} / {i.MaxPlayers}";
        _tiles[0].Sub.Text = $"{i.Queued} queued, {i.Joining} joining";
        _tiles[1].Value.Text = i.Fps.ToString("0");
        _tiles[1].Sub.Text = "server frame rate";
        _tiles[2].Value.Text = i.Entities.ToString("N0");
        _tiles[2].Sub.Text = "world entities";
        var up = TimeSpan.FromSeconds(i.UptimeSeconds);
        _tiles[3].Value.Text = up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h" : $"{up.Hours}h {up.Minutes}m";
        _tiles[3].Sub.Text = "since last restart";
        _tiles[4].Value.Text = i.MemoryMb + " MB";
        _tiles[4].Sub.Text = "in use";
        _tiles[5].Value.Text = string.IsNullOrEmpty(i.Map) ? "-" : i.Map;
        _tiles[5].Sub.Text = i.Hostname;
        foreach (var (v, s) in _tiles) { Tips.Set(v, v.Text); Tips.Set(s, s.Text); }
    }
}

/// <summary>Builds a widget (title, content, header actions) for a WidgetKind.</summary>
public static class Widgets
{
    public static readonly WidgetKind[] All =
    {
        WidgetKind.Console, WidgetKind.Chat, WidgetKind.Players, WidgetKind.ServerInfo, WidgetKind.GiveItems,
        WidgetKind.Announce, WidgetKind.QuickActions, WidgetKind.Moderation, WidgetKind.Teleport, WidgetKind.Shortcuts,
        WidgetKind.Health, WidgetKind.Scheduler, WidgetKind.PlayerDb, WidgetKind.Activity, WidgetKind.ChatMonitor,
    };

    public static string Name(WidgetKind k) => k switch
    {
        WidgetKind.Console => "Console",
        WidgetKind.Chat => "Chat",
        WidgetKind.Players => "Players",
        WidgetKind.GiveItems => "Give items",
        WidgetKind.Announce => "Announce",
        WidgetKind.QuickActions => "Quick actions",
        WidgetKind.Moderation => "Moderation",
        WidgetKind.Teleport => "Teleport",
        WidgetKind.Shortcuts => "Shortcuts",
        WidgetKind.Custom => "Community widget",
        WidgetKind.Health => "Server health",
        WidgetKind.Scheduler => "Scheduler",
        WidgetKind.PlayerDb => "Player database",
        WidgetKind.Activity => "Join and leave feed",
        WidgetKind.ChatMonitor => "Chat monitor",
        _ => "Server info",
    };

    /// <summary>Returns the widget's content and the actions its card should show.</summary>
    public static (Control Content, Control[] Actions) Create(WidgetKind kind, Core core, int infoColumns = 2, string? customId = null)
    {
        switch (kind)
        {
            case WidgetKind.Chat: { var v = new ConsoleView(core, chat: true); return (v, v.HeaderActions); }
            case WidgetKind.Players: { var v = new PlayersView(core); return (v, v.HeaderActions); }
            case WidgetKind.ServerInfo: return (new ServerInfoView(core, infoColumns), Array.Empty<Control>());
            case WidgetKind.GiveItems: return (new GiveItemsView(core), Array.Empty<Control>());
            case WidgetKind.Announce: return (new AnnounceView(core), Array.Empty<Control>());
            case WidgetKind.QuickActions: return (new QuickActionsView(core), Array.Empty<Control>());
            case WidgetKind.Moderation: return (new ModerationView(core), Array.Empty<Control>());
            case WidgetKind.Teleport: return (new TeleportView(core), Array.Empty<Control>());
            case WidgetKind.Shortcuts: return (new ShortcutsView(core), Array.Empty<Control>());
            case WidgetKind.Health: return (new HealthView(core), Array.Empty<Control>());
            case WidgetKind.Scheduler: return (new SchedulerView(core), Array.Empty<Control>());
            case WidgetKind.PlayerDb: return (new PlayerDbView(core), Array.Empty<Control>());
            case WidgetKind.Activity: return (new ActivityView(core), Array.Empty<Control>());
            case WidgetKind.ChatMonitor: return (new ChatMonitorView(core), Array.Empty<Control>());
            case WidgetKind.Custom:
                return core.Widgets.Get(customId) is { } def
                    ? (new CustomWidgetView(core, def), Array.Empty<Control>())
                    : (new Label { Text = "This community widget is not installed any more. Pick another widget from the title menu, or install it again from the Widgets page.", ForeColor = Theme.Muted, Padding = new Padding(4, 14, 4, 0) }, Array.Empty<Control>());
            default: { var v = new ConsoleView(core); return (v, v.HeaderActions); }
        }
    }

    public static Card CreateCard(WidgetKind kind, Core core, int infoColumns = 2)
    {
        var (content, actions) = Create(kind, core, infoColumns);
        return Card.Titled(Name(kind), content, actions);
    }
}
