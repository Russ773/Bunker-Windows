using System.Reflection;

namespace Bunker;

/// <summary>Builds the commands that post chat from the Chat panel, as you (name, Steam ID, colour) or as plain "say".</summary>
public static class ChatSender
{
    public const string PluginUrl = "https://raw.githubusercontent.com/Russ773/Bunker-Windows/main/plugins/BunkerChat.cs";
    public const string DefaultColour = "#af5";

    public static bool IsSteamId(string? text) => text is { Length: 17 } && text.All(char.IsDigit);

    /// <summary>The colour as "#rrggbb"-style text the game understands, or the admin green if it is not a colour.</summary>
    public static string ColourOrDefault(string? colour) =>
        Theme.ParseHex(colour) is null ? DefaultColour : "#" + colour!.Trim().TrimStart('#');

    public const string DefaultTagColour = "#5af";

    /// <summary>Like <see cref="ColourOrDefault"/>, for the colour of plugin prefixes such as [ADMIN] (blue unless you choose otherwise).</summary>
    public static string TagColourOrDefault(string? colour) =>
        Theme.ParseHex(colour) is null ? DefaultTagColour : "#" + colour!.Trim().TrimStart('#');

    /// <summary>The command for the Bunker Chat plugin on the server: <c>bunker.say steamid "name" "colour" "message"</c>.</summary>
    public static string PluginCommand(string steamId, string name, string colour, string message) =>
        $"bunker.say {(IsSteamId(steamId) ? steamId : "0")} {Cmd.Quote(name)} {Cmd.Quote(ColourOrDefault(colour))} {Cmd.Quote(message)}";

    /// <summary>The plain command Rust has built in. It always posts as SERVER, so a name is added to the text when you have one.</summary>
    public static string SayCommand(string name, string message) =>
        name.Trim().Length == 0 ? "say " + message : "say " + Cmd.Quote(name.Trim() + ": " + message);

    /// <summary>True when a server reply means it does not know the command (the plugin is not installed).</summary>
    public static bool LooksUnknown(string reply) =>
        reply.Contains("unknown command", StringComparison.OrdinalIgnoreCase) || reply.Contains("not found", StringComparison.OrdinalIgnoreCase);

    /// <summary>The source code of the Bunker Chat server plugin, built into the app so it can be copied without a download.</summary>
    public static string PluginSource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BunkerChat.cs");
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
