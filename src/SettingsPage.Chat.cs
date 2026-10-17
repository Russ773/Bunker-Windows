using System.Diagnostics;
using System.Drawing;

namespace Bunker;

/// <summary>The Chat tab in Settings: the name, Steam ID and colour you appear with when you send chat from Bunker.</summary>
public partial class SettingsPage
{
    // The preview is made of labels (one per colour) rather than a RichTextBox, whose colours do not apply until it has been shown.
    private readonly FlowLayoutPanel _chatPreview = new()
    {
        AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(12, 10, 12, 10), Margin = new Padding(0, 6, 0, 0), MinimumSize = new Size(420, 0),
    };
    private readonly Label _previewTime = new() { AutoSize = true, ForeColor = Theme.Dim, Margin = new Padding(0), Text = "17:30:43  " };
    private readonly Label _previewTag = new() { AutoSize = true, Margin = new Padding(0), Font = new Font("Segoe UI Semibold", 10f), Text = "[ADMIN] " };
    private readonly Label _previewName = new() { AutoSize = true, Margin = new Padding(0), Font = new Font("Segoe UI Semibold", 10f) };
    private readonly Label _previewColon = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0), Text = ": " };
    private readonly Label _previewText = new() { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0), Text = "Hello everyone" };

    private Control BuildChat()
    {
        var s = _core.Settings;
        var inner = new Panel { BackColor = Theme.Panel, AutoScroll = true };
        DarkScroll.Apply(inner);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel, Padding = new Padding(2, 4, 0, 16) };

        Label Note(string text, Color? colour = null, int top = 0) => new()
        {
            Text = text, ForeColor = colour ?? Theme.Muted, Font = Theme.Small, AutoSize = true, MaximumSize = new Size(620, 0), Margin = new Padding(0, top, 0, 0),
        };
        Label Caption(string text) => new() { Text = text, ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = true, Margin = new Padding(0, 16, 0, 4) };

        flow.Controls.Add(Note("Choose how you appear when you send a message from the Chat panel. Without a name, Rust posts it as SERVER."));

        // Name
        var name = new ModernField { Width = 420, Text = s.ChatName };
        name.Box.MaxLength = 40;
        name.Box.PlaceholderText = "Your name in chat, for example Russ";
        flow.Controls.Add(Caption("Chat name"));
        flow.Controls.Add(name);

        // Steam ID
        var steam = new ModernField { Width = 420, Text = s.ChatSteamId };
        steam.Box.MaxLength = 17;
        steam.Box.PlaceholderText = "17 digits, for example 76561198000000001";
        steam.Box.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
        var steamNote = Note("", top: 6);
        flow.Controls.Add(Caption("Steam ID (shows your Steam picture next to the message)"));
        flow.Controls.Add(steam);
        flow.Controls.Add(steamNote);

        // Colour
        var colour = new ModernField { Width = 140, Text = s.ChatColor };
        colour.Box.MaxLength = 9;
        var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = new Padding(0) };
        foreach (var (label, hex) in new[] { ("Admin green", "#af5"), ("Player blue", "#5af"), ("Orange", "#fa5"), ("White", "#eee") })
        {
            var b = Theme.GhostButton(label);
            b.Height = 38;
            b.Margin = new Padding(0, 0, 8, 0);
            var value = hex;
            b.Click += (_, _) => colour.Text = value;
            presets.Controls.Add(b);
        }
        colour.Margin = new Padding(8, 0, 0, 0);
        presets.Controls.Add(colour);
        flow.Controls.Add(Caption("Name colour"));
        flow.Controls.Add(presets);

        // Tag colour: the prefixes a chat plugin adds, such as [ADMIN]
        var tagColour = new ModernField { Width = 140, Text = s.ChatTagColor };
        tagColour.Box.MaxLength = 9;
        var tagPresets = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = new Padding(0) };
        foreach (var (label, hex) in new[] { ("Blue", "#5af"), ("Green", "#af5"), ("Orange", "#fa5"), ("White", "#eee") })
        {
            var b = Theme.GhostButton(label);
            b.Height = 38;
            b.Margin = new Padding(0, 0, 8, 0);
            var value = hex;
            b.Click += (_, _) => tagColour.Text = value;
            tagPresets.Controls.Add(b);
        }
        tagColour.Margin = new Padding(8, 0, 0, 0);
        tagPresets.Controls.Add(tagColour);
        flow.Controls.Add(Caption("Tag colour (for prefixes such as [ADMIN] that your chat plugin adds; the server does not tell Bunker their colour)"));
        flow.Controls.Add(tagPresets);

        // Preview
        _chatPreview.Controls.AddRange(new Control[] { _previewTime, _previewTag, _previewName, _previewColon, _previewText });
        flow.Controls.Add(Caption("Preview"));
        flow.Controls.Add(_chatPreview);

        // Plugin
        var usePlugin = new CheckBox
        {
            Text = "Send chat through the Bunker Chat plugin on the server (recommended)", AutoSize = true, ForeColor = Theme.Text,
            Cursor = Cursors.Hand, Checked = s.ChatUsePlugin, Margin = new Padding(0, 22, 0, 0),
        };
        flow.Controls.Add(usePlugin);
        flow.Controls.Add(Note(
            "Rust has no built-in way to post chat as a named player, so the server needs one small plugin. It adds a single admin-only command, " +
            "bunker.say, and works on Carbon and Oxide. Save it as BunkerChat.cs in your server's carbon/plugins (Carbon) or oxide/plugins (Oxide) folder and it loads by itself. " +
            "Until it is installed, Bunker sends the message with the plain say command, with your name at the start of the text.", top: 6));

        var copy = Theme.PrimaryButton("Copy plugin code");
        var github = Theme.GhostButton("Open on GitHub");
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = new Padding(0, 12, 0, 0) };
        copy.Height = github.Height = 38;
        copy.Margin = new Padding(0, 0, 8, 0);
        actions.Controls.Add(copy);
        actions.Controls.Add(github);
        flow.Controls.Add(actions);
        var copied = Note("", Theme.Ok, top: 8);
        flow.Controls.Add(copied);
        copy.Click += (_, _) =>
        {
            var source = ChatSender.PluginSource();
            if (source.Length == 0) { copied.ForeColor = Theme.Err; copied.Text = "The plugin text is missing from this build."; return; }
            try { Clipboard.SetText(source); copied.ForeColor = Theme.Ok; copied.Text = "Copied. Paste it into a new file named BunkerChat.cs on the server."; }
            catch { copied.ForeColor = Theme.Err; copied.Text = "Could not use the clipboard. Use Open on GitHub instead."; }
        };
        github.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("https://github.com/Russ773/Bunker-Windows/blob/main/plugins/BunkerChat.cs") { UseShellExecute = true }); } catch { } };

        void Refresh()
        {
            var shown = ChatSender.ColourOrDefault(colour.Text);
            var tint = Theme.Readable(Theme.ParseHex(shown) ?? Theme.Chat);
            var who = string.IsNullOrWhiteSpace(name.Text) ? "SERVER" : name.Text.Trim();
            _previewName.Text = who;
            _previewName.ForeColor = tint;
            _previewTag.ForeColor = Theme.Readable(Theme.ParseHex(ChatSender.TagColourOrDefault(tagColour.Text)) ?? Theme.Chat);

            var id = steam.Text.Trim();
            steamNote.Text = id.Length == 0 ? "Optional. Without it your message has no Steam picture."
                : ChatSender.IsSteamId(id) ? "Looks right." : "A Steam ID is 17 digits.";
            steamNote.ForeColor = id.Length == 0 || ChatSender.IsSteamId(id) ? Theme.Muted : Theme.Warn;
        }

        void Save()
        {
            s.ChatName = name.Text.Trim();
            s.ChatSteamId = steam.Text.Trim();
            s.ChatColor = ChatSender.ColourOrDefault(colour.Text);
            s.ChatTagColor = ChatSender.TagColourOrDefault(tagColour.Text);
            s.ChatUsePlugin = usePlugin.Checked;
            s.Save();
            _core.NotifyChatIdentityChanged();
            Refresh();
        }

        name.Box.TextChanged += (_, _) => Save();
        steam.Box.TextChanged += (_, _) => Save();
        colour.Box.TextChanged += (_, _) => Save();
        tagColour.Box.TextChanged += (_, _) => Save();
        usePlugin.CheckedChanged += (_, _) => Save();
        Refresh();

        inner.Controls.Add(flow);
        return Wrap("Chat", inner);
    }
}
