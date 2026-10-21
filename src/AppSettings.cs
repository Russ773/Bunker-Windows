using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bunker;

/// <summary>Encrypts a small secret for the current Windows user, the way the RCON passwords are.</summary>
internal static class Secret
{
    public static string? Protect(string? value) => string.IsNullOrEmpty(value)
        ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}

public class ServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 28016;

    /// <summary>The RCON password, encrypted with Windows DPAPI (only this Windows user can decrypt it).</summary>
    public string? PasswordProtected { get; set; }

    [JsonIgnore]
    public string Password
    {
        get
        {
            if (string.IsNullOrEmpty(PasswordProtected)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(PasswordProtected), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
        set => PasswordProtected = string.IsNullOrEmpty(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Host : Name;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WidgetKind { Console, Chat, Players, ServerInfo, GiveItems, Announce, QuickActions, Moderation, Teleport, Shortcuts, Custom, Health, Scheduler, PlayerDb, Activity, ChatMonitor }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LayoutPreset { Single, TwoColumns, TwoRows, ThreeLeftBig, FourGrid }

/// <summary>The user's custom view: a preset arrangement plus which widget fills each slot.</summary>
public class LayoutConfig
{
    public LayoutPreset Preset { get; set; } = LayoutPreset.FourGrid;
    public WidgetKind[] Slots { get; set; } = { WidgetKind.Players, WidgetKind.Console, WidgetKind.ServerInfo, WidgetKind.Chat };

    [JsonIgnore]
    public int SlotCount => Preset switch
    {
        LayoutPreset.Single => 1,
        LayoutPreset.TwoColumns or LayoutPreset.TwoRows => 2,
        LayoutPreset.ThreeLeftBig => 3,
        _ => 4,
    };

    /// <summary>Which community widget fills each slot whose kind is Custom.</summary>
    public string?[] CustomIds { get; set; } = new string?[4];

    public string? CustomAt(int i) => CustomIds is { Length: > 0 } && i < CustomIds.Length ? CustomIds[i] : null;

    public WidgetKind SlotAt(int i) => Slots is { Length: > 0 } && i < Slots.Length ? Slots[i] : WidgetKind.Console;
}

/// <summary>A user-defined button that runs a console command.</summary>
public class CommandShortcut
{
    public string Label { get; set; } = "";
    public string Command { get; set; } = "";
    public bool Confirm { get; set; }
}

/// <summary>A command (or list of commands) Bunker runs on a timer while connected.</summary>
public class ScheduledTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> Commands { get; set; } = new();
    /// <summary>"interval" (every N minutes) or "daily" (at a time of day).</summary>
    public string Mode { get; set; } = "interval";
    public int IntervalMinutes { get; set; } = 60;
    public string TimeOfDay { get; set; } = "04:00";
    public bool Enabled { get; set; } = true;
    public DateTime? LastRunUtc { get; set; }
}

public class AppSettings
{
    public bool CheckForUpdatesOnLaunch { get; set; } = true;
    public bool StartMaximised { get; set; } = true;
    public bool AutoConnect { get; set; } = true;
    /// <summary>The version that last ran, so the "what's new" note shows once after an update.</summary>
    public string LastSeenVersion { get; set; } = "";
    /// <summary>True when there was no settings file yet (a fresh install, which has nothing to be told about).</summary>
    [JsonIgnore]
    public bool IsFirstRun { get; private set; }
    /// <summary>Try to reconnect by itself when the connection to the server drops.</summary>
    public bool AutoReconnect { get; set; } = true;
    /// <summary>Look a player up on Steam (public profile and Rust hours) when their card is opened. Only their Steam ID is sent.</summary>
    public bool SteamLookups { get; set; } = true;
    /// <summary>Your free Steam Web API key (optional), encrypted with Windows DPAPI like the RCON passwords. It adds Rust hours, account age and the full ban record to player cards.</summary>
    public string? SteamApiKeyProtected { get; set; }

    [JsonIgnore]
    public string SteamApiKey
    {
        get => Secret.Unprotect(SteamApiKeyProtected);
        set => SteamApiKeyProtected = Secret.Protect(value);
    }
    /// <summary>Which saved servers the Multi Server page shows when there are more than six. Empty means the first six.</summary>
    public List<string> OverviewServerIds { get; set; } = new();
    /// <summary>Show the Bunker icon in the system tray (menu to show, connect or quit).</summary>
    public bool ShowTrayIcon { get; set; } = true;
    /// <summary>Minimising takes Bunker off the taskbar so only the tray icon is left. Needs the tray icon.</summary>
    public bool TrayHideWhenMinimised { get; set; }
    /// <summary>Windows notifications for chat watch-words and a lost connection while Bunker is not in front. Needs the tray icon.</summary>
    public bool TrayNotifications { get; set; } = true;
    /// <summary>What the X in the corner of the window does: "ask", "minimise" or "close".</summary>
    public string CloseButton { get; set; } = CloseButtonSetting.Ask;
    /// <summary>Download the Rust item list and pictures from GitHub (bobosneefdev/rust-items-json) so item search stays current.</summary>
    /// <summary>Widgets hidden from the sidebar menu (by key). They stay available in the Layout picker.</summary>
    public List<string> HiddenWidgets { get; set; } = new();
    /// <summary>The name, Steam ID and name colour used when you send chat from the Chat panel (instead of SERVER).</summary>
    public string ChatName { get; set; } = "";
    public string ChatSteamId { get; set; } = "";
    public string ChatColor { get; set; } = "#af5";
    /// <summary>Send chat through the Bunker Chat plugin on the server (shows your name and avatar). Falls back to the plain say command if it is not installed.</summary>
    public bool ChatUsePlugin { get; set; } = true;
    /// <summary>The colour of prefixes that chat plugins add, such as [ADMIN]. The server does not say what colour the game shows them in.</summary>
    public string ChatTagColor { get; set; } = "#5af";
    /// <summary>Widgets you starred. They are listed first in the WIDGETS menu.</summary>
    public List<string> FavouriteWidgets { get; set; } = new();
    /// <summary>Rules for console lines to hide: a whole plugin, or any line containing some words.</summary>
    public List<HideRule> ConsoleHidden { get; set; } = new();
    public bool UseOnlineItemList { get; set; } = true;
    public bool LightTheme { get; set; }
    public string ConsoleFontName { get; set; } = "Consolas";
    public float ConsoleFontSize { get; set; } = 10.5f;
    public List<ServerProfile> Servers { get; set; } = new();
    public string? ActiveServerId { get; set; }
    public LayoutConfig Layout { get; set; } = new();
    public List<string> SavedAnnouncements { get; set; } = new();
    public List<CommandShortcut> Shortcuts { get; set; } = new();
    public List<ScheduledTask> Schedules { get; set; } = new();
    public string ChatWatchWords { get; set; } = "";
    public bool ChatFlash { get; set; } = true;
    public bool ChatSound { get; set; }

    /// <summary>The single server saved by older versions. Moved into Servers the first time settings load.</summary>
    [JsonPropertyName("Server")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServerProfile? LegacyServer { get; set; }

    [JsonIgnore]
    public ServerProfile? ActiveServer => Servers.FirstOrDefault(s => s.Id == ActiveServerId) ?? Servers.FirstOrDefault();

    public void AddOrUpdateServer(ServerProfile profile, bool makeActive)
    {
        var i = Servers.FindIndex(s => s.Id == profile.Id);
        if (i >= 0) Servers[i] = profile; else Servers.Add(profile);
        if (makeActive || ActiveServerId is null) ActiveServerId = profile.Id;
    }

    public void RemoveServer(string id)
    {
        Servers.RemoveAll(s => s.Id == id);
        if (ActiveServerId == id) ActiveServerId = Servers.FirstOrDefault()?.Id;
    }

    // BUNKER_DATA_DIR lets tests run against a throwaway settings folder instead of the real one.
    private static readonly string FilePath = Path.Combine(Environment.GetEnvironmentVariable("BUNKER_DATA_DIR") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bunker"), "settings.json");

    public static string Folder => Path.GetDirectoryName(FilePath)!;

    public static AppSettings Load()
    {
        var existed = File.Exists(FilePath);
        AppSettings s;
        try { s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new() { IsFirstRun = !existed }; }
        s.IsFirstRun = !existed;

        if (s.LegacyServer is not null)
        {
            if (s.Servers.Count == 0) s.Servers.Add(s.LegacyServer);
            s.ActiveServerId ??= s.LegacyServer.Id;
            s.LegacyServer = null;
            try { s.Save(); } catch { }
        }
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
