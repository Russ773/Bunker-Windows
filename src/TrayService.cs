using System.Drawing;
using System.Runtime.InteropServices;

namespace Bunker;

/// <summary>The words a chat alert looks for, shared by the Chat monitor page and the tray notifications.</summary>
public static class WatchWords
{
    public static List<string> Parse(string? text) =>
        (text ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(w => w.Length > 0).ToList();

    public static bool Matches(string? words, string message) =>
        Parse(words) is { Count: > 0 } list && list.Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Helpers for the text shown by the tray icon. Kept separate so they can be tested.</summary>
public static class TrayText
{
    /// <summary>Windows cuts the tray tooltip off at 63 characters.</summary>
    public const int TooltipLimit = 63;

    public static string Tooltip(string server, string status)
    {
        var text = string.IsNullOrWhiteSpace(server) ? $"Bunker: {status}" : $"Bunker ({server}): {status}";
        return text.Length <= TooltipLimit ? text : text[..(TooltipLimit - 1)] + "…";
    }

    /// <summary>Balloon notifications have a short body, so long chat messages are trimmed.</summary>
    public static string Trim(string text, int max = 120)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}

/// <summary>
/// The Bunker icon in the system tray: a menu to show the window, connect or quit, and Windows notifications for chat watch-words
/// and a lost connection while Bunker is not in front.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly MainForm _form;
    private readonly Core _core;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggle;
    private DateTime _lastChatToast = DateTime.MinValue;

    public TrayService(MainForm form, Core core)
    {
        _form = form;
        _core = core;

        var menu = new ContextMenuStrip { BackColor = Theme.Input, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
        ToolStripMenuItem Item(string text, Action action) => new(text, null, (_, _) => action()) { BackColor = Theme.Input, ForeColor = Theme.Text };
        _toggle = Item("Connect", () => _ = ToggleAsync());
        menu.Items.Add(Item("Show Bunker", Restore));
        menu.Items.Add(_toggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Quit Bunker", () => Application.Exit()));

        _icon = new NotifyIcon { Icon = Theme.AppIcon ?? SystemIcons.Application, Text = "Bunker", ContextMenuStrip = menu, Visible = false };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Restore(); };
        _icon.BalloonTipClicked += (_, _) => Restore();

        _core.StateChanged += Refresh;
        _core.ServerChanged += Refresh;
        _core.TraySettingsChanged += Refresh;
        _core.Log.Added += OnLogAdded;
        _core.ConnectionLost += OnConnectionLost;
        _core.Reconnected += OnReconnected;
        _form.Resize += OnFormResize;
        Refresh();
    }

    /// <summary>Brings the window back from the tray or the taskbar.</summary>
    public void Restore() => _form.BringForward();

    private async Task ToggleAsync()
    {
        if (_core.State == ConnState.Connected) await _core.DisconnectAsync();
        else if (_core.Settings.ActiveServer is not null) await _core.ConnectAsync();
        else Restore();
    }

    private void Refresh()
    {
        _icon.Visible = _core.Settings.ShowTrayIcon;
        _toggle.Text = _core.State == ConnState.Connected ? "Disconnect" : "Connect";
        _icon.Text = TrayText.Tooltip(_core.Settings.ActiveServer?.DisplayName ?? "", _core.StatusText);
    }

    // When the window is minimised and the tray icon is on, "hide when minimised" takes the taskbar button away so only the tray icon is left.
    private void OnFormResize(object? sender, EventArgs e)
    {
        if (_form.WindowState == FormWindowState.Minimized && _core.Settings.ShowTrayIcon && _core.Settings.TrayHideWhenMinimised) _form.Hide();
    }

    private bool InFront => _form.Visible && _form.WindowState != FormWindowState.Minimized && Form.ActiveForm == _form;

    private void Toast(string title, string text)
    {
        if (!_core.Settings.ShowTrayIcon || !_core.Settings.TrayNotifications || InFront) return;
        _icon.ShowBalloonTip(6000, title, TrayText.Trim(text), ToolTipIcon.Info);
    }

    private void OnLogAdded(LogEntry e)
    {
        if (e.Kind != LogKind.Chat || !WatchWords.Matches(_core.Settings.ChatWatchWords, e.Text)) return;
        if (DateTime.UtcNow - _lastChatToast < TimeSpan.FromSeconds(8)) return; // a burst of matches should not bury the screen in notifications
        _lastChatToast = DateTime.UtcNow;
        Toast(string.IsNullOrEmpty(e.UserName) ? "Chat alert" : "Chat alert: " + e.UserName, e.Message ?? e.Text);
    }

    private void OnConnectionLost() => Toast("Connection lost", _core.Settings.AutoReconnect ? "Bunker is trying to reconnect." : "Bunker is no longer connected to the server.");

    private void OnReconnected() => Toast("Reconnected", "Bunker is connected to the server again.");

    public void Dispose()
    {
        _core.StateChanged -= Refresh;
        _core.ServerChanged -= Refresh;
        _core.TraySettingsChanged -= Refresh;
        _core.Log.Added -= OnLogAdded;
        _core.ConnectionLost -= OnConnectionLost;
        _core.Reconnected -= OnReconnected;
        _form.Resize -= OnFormResize;
        _icon.Visible = false; // otherwise the icon lingers in the tray until the mouse is moved over it
        _icon.Dispose();
    }
}
