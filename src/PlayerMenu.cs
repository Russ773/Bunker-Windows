using System.Diagnostics;
using System.Drawing;

namespace Bunker;

/// <summary>One text box in a <see cref="PromptForm"/>.</summary>
public sealed record PromptField(string Label, string Default = "", string Placeholder = "", bool DigitsOnly = false, string[]? Suggestions = null, bool Multiline = false, Core? ItemSearchFor = null);

/// <summary>A small themed dialog that asks for one or two pieces of text, such as a reason or an item and amount.</summary>
public class PromptForm : Form
{
    private readonly ModernField[] _fields;

    public string[] Values => _fields.Select(f => f.Text.Trim()).ToArray();

    public PromptForm(string title, string message, string okText, params PromptField[] fields)
    {
        Theme.Apply(this);
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var ok = Theme.PrimaryButton(okText);
        var cancel = Theme.GhostButton("Cancel");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 22, 28, 16) };
        if (message.Length > 0)
            flow.Controls.Add(new Label { Text = message, ForeColor = Theme.Text, AutoSize = true, MaximumSize = new Size(444, 0), Margin = new Padding(0, 0, 0, 6) });

        _fields = new ModernField[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            flow.Controls.Add(new Label { Text = f.Label, ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Margin = new Padding(0, 8, 0, 3) });
            var box = f.ItemSearchFor is { } itemCore ? new ItemPicker(itemCore) { Width = 444, Text = f.Default } : new ModernField { Width = 444, Text = f.Default };
            if (f.Placeholder.Length > 0) box.Box.PlaceholderText = f.Placeholder;
            if (f.DigitsOnly) box.Box.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            if (f.Suggestions is { Length: > 0 })
            {
                var source = new AutoCompleteStringCollection();
                source.AddRange(f.Suggestions);
                box.Box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                box.Box.AutoCompleteSource = AutoCompleteSource.CustomSource;
                box.Box.AutoCompleteCustomSource = source;
            }
            _fields[i] = box;
            flow.Controls.Add(box);
        }

        var bar = new FlowLayoutPanel { Width = 444, Height = 48, Margin = new Padding(0, 16, 0, 0), BackColor = Theme.Bg };
        ok.Margin = new Padding(0, 0, 8, 0);
        bar.Controls.Add(ok);
        bar.Controls.Add(cancel);
        flow.Controls.Add(bar);

        Controls.Add(flow);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 4, BackColor = Theme.Accent });
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(500, 120 + (message.Length > 0 ? 40 + message.Length / 60 * 18 : 0) + fields.Length * 66);
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Shown += (_, _) => { if (_fields.Length > 0) { _fields[0].Box.Focus(); _fields[0].Box.SelectAll(); } };
    }

    /// <summary>Shows the dialog and returns what was typed, or null if it was cancelled.</summary>
    public static string[]? Ask(IWin32Window? owner, string title, string message, string okText, params PromptField[] fields)
    {
        using var form = new PromptForm(title, message, okText, fields);
        return form.ShowDialog(owner) == DialogResult.OK ? form.Values : null;
    }
}

/// <summary>Who the player menu is for. Players who are not online can still be copied, looked up, banned or given a note.</summary>
public sealed record PlayerTarget(string SteamId, string Name, int? Ping, bool Online);

/// <summary>The right-click menu for a player: copy, open their Steam profile, moderate, teleport, give items and keep a note.</summary>
public static class PlayerMenu
{
    /// <summary>Gives a grid of players a themed right-click menu. The selected player is read from <paramref name="selected"/> each time it opens.</summary>
    /// <summary>For a table of players who are online right now.</summary>
    public static void Attach(Control grid, Core core, Func<PlayerInfo?> selected) =>
        Attach(grid, core, () => selected() is { } p ? new PlayerTarget(p.SteamId, p.Name, p.Ping, true) : null);

    /// <summary>For any table of players, such as the player database, where some are offline.</summary>
    public static void Attach(Control grid, Core core, Func<PlayerTarget?> selected)
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        menu.Opening += (_, e) =>
        {
            if (selected() is not { } player) { e.Cancel = true; return; }
            Rebuild(menu, core, player, grid);
        };
        grid.ContextMenuStrip = menu;
    }

    private static ToolStripMenuItem Item(string text, Action? click, bool enabled = true, Color? colour = null)
    {
        var item = new ToolStripMenuItem(text) { BackColor = Theme.Input, ForeColor = colour ?? Theme.Text, Enabled = enabled };
        if (click is not null) item.Click += (_, _) => click();
        return item;
    }

    private static ToolStripMenuItem Heading(string text) => Item(text, null, enabled: false, colour: Theme.Muted);

    internal static void Rebuild(ContextMenuStrip menu, Core core, PlayerTarget p, Control owner)
    {
        menu.Items.Clear();
        var form = owner.FindForm();
        var connected = core.State == ConnState.Connected;
        var online = connected && p.Online; // kick, mute, teleport and give need the player to be on the server now
        var safeId = p.SteamId.All(char.IsDigit);

        menu.Items.Add(Heading(p.Online && p.Ping is { } ping ? $"{p.Name}   ·   {ping} ms" : p.Online ? p.Name : $"{p.Name}   ·   offline"));
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(Item("Copy name", () => SafeCopy(p.Name)));
        menu.Items.Add(Item("Copy Steam ID", () => SafeCopy(p.SteamId)));
        menu.Items.Add(Item("Open Steam profile", () =>
        {
            try { Process.Start(new ProcessStartInfo($"https://steamcommunity.com/profiles/{p.SteamId}") { UseShellExecute = true }); } catch { }
        }, enabled: safeId));
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(Item("Kick...", () => _ = Moderate(core, form, p, "kick"), online && safeId));
        menu.Items.Add(Item("Ban...", () => _ = Moderate(core, form, p, "ban"), connected && safeId, Theme.Err)); // a ban works for players who are offline too
        menu.Items.Add(Item("Mute", () => _ = Moderate(core, form, p, "mute"), online && safeId));
        menu.Items.Add(Item("Unmute", () => _ = Moderate(core, form, p, "unmute"), online && safeId));
        menu.Items.Add(new ToolStripSeparator());

        var others = core.Monitor.Players.Where(o => o.SteamId != p.SteamId && o.SteamId.All(char.IsDigit))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        var sendTo = Item($"Teleport {Short(p.Name)} to...", null, online && safeId && others.Count > 0);
        var bring = Item($"Teleport to {Short(p.Name)}...", null, online && safeId && others.Count > 0);
        foreach (var o in others)
        {
            var other = o;
            sendTo.DropDownItems.Add(Item(other.Name, () => _ = core.SendCommandAsync($"teleport \"{p.SteamId}\" \"{other.SteamId}\"")));
            bring.DropDownItems.Add(Item(other.Name, () => _ = core.SendCommandAsync($"teleport \"{other.SteamId}\" \"{p.SteamId}\"")));
        }
        foreach (var sub in new[] { sendTo, bring }) sub.DropDown.BackColor = Theme.Input;
        menu.Items.Add(sendTo);
        menu.Items.Add(bring);
        menu.Items.Add(Item("Give item...", () => _ = GiveItem(core, form, p), online && safeId));
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(Item("Add or edit note...", () => EditNote(core, form, p)));
    }

    private static string Short(string name) => name.Length <= 18 ? name : name[..17] + "…";

    private static void SafeCopy(string text)
    {
        try { Clipboard.SetText(text); } catch { /* the clipboard can be busy */ }
    }

    internal static async Task Moderate(Core core, IWin32Window? owner, PlayerTarget p, string verb)
    {
        switch (verb)
        {
            case "kick" or "ban":
            {
                var title = verb == "ban" ? "Ban player" : "Kick player";
                var asked = PromptForm.Ask(owner, title, $"{(verb == "ban" ? "Ban" : "Kick")} {p.Name} ({p.SteamId})?", verb == "ban" ? "Ban" : "Kick",
                    new PromptField("Reason (the player sees this)", verb == "ban" ? "Banned by admin" : "Kicked by admin"));
                if (asked is null) return;
                var reason = asked[0].Length == 0 ? (verb == "ban" ? "Banned by admin" : "Kicked by admin") : asked[0];
                if (verb == "ban")
                {
                    await core.SendCommandAsync($"banid {p.SteamId} {Cmd.Quote(p.Name)} {Cmd.Quote(reason)}");
                    await core.SendCommandAsync("server.writecfg");
                }
                else await core.SendCommandAsync($"kick \"{p.SteamId}\" {Cmd.Quote(reason)}");
                break;
            }
            default:
            {
                var label = verb == "mute" ? "Mute" : "Unmute";
                if (MessageBox.Show(owner, $"{label} {p.Name}?", $"{label} player", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                await core.SendCommandAsync($"{verb} \"{p.SteamId}\"");
                break;
            }
        }
    }

    internal static async Task GiveItem(Core core, IWin32Window? owner, PlayerTarget p)
    {
        while (true)
        {
            var asked = PromptForm.Ask(owner, "Give item", $"Give an item to {p.Name}.", "Give",
                new PromptField("Item (type to search)", "", ItemSearchFor: core),
                new PromptField("Amount", "100", DigitsOnly: true));
            if (asked is null) return;
            if (!GiveItemsView.SafeItem.IsMatch(asked[0])) { MessageBox.Show(owner, "Enter an item shortname such as wood or rifle.ak (letters, numbers, dots and dashes only).", "Give item"); continue; }
            if (!int.TryParse(asked[1], out var amount) || amount is < 1 or > 100000) { MessageBox.Show(owner, "The amount must be a number from 1 to 100,000.", "Give item"); continue; }
            await core.SendCommandAsync($"inventory.giveto \"{p.SteamId}\" \"{asked[0]}\" {amount}");
            return;
        }
    }

    private static void EditNote(Core core, IWin32Window? owner, PlayerTarget p)
    {
        var existing = core.Tracker.Records.FirstOrDefault(r => r.SteamId == p.SteamId)?.Note ?? "";
        var asked = PromptForm.Ask(owner, "Player note", $"A private note about {p.Name}. It is saved on this PC and shown in the Player database.", "Save",
            new PromptField("Note", existing));
        if (asked is not null) core.Tracker.SetNote(p.SteamId, asked[0]);
    }
}
