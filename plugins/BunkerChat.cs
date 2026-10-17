// Bunker Chat: lets the Bunker admin app post chat as a named admin.
//
// Rust's built-in "say" command always posts as "SERVER". This small plugin adds one console command, bunker.say, that posts a
// message the way the game shows a normal player message: with the admin's name, name colour and Steam avatar.
//
// Install: save this file as BunkerChat.cs in your server's  carbon/plugins  folder (Carbon) or  oxide/plugins  folder (Oxide/uMod).
// It loads by itself. Only the server console, RCON and in-game admins can use the command.
//
// Usage from the console or RCON:   bunker.say <steamid> <name> <colour> <message>
//   bunker.say 76561198000000001 "Russ" "#af5" "Hello everyone"
//
// Bunker Chat is part of the Bunker project (MIT licence): https://github.com/Russ773/Bunker-Windows

using System.Text;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
    [Info("Bunker Chat", "Bunker", "1.0.0")]
    [Description("Lets the Bunker admin app post chat with an admin's name, colour and avatar instead of SERVER.")]
    public class BunkerChat : RustPlugin
    {
        private static readonly Regex Colour = new Regex("^#[0-9a-fA-F]{3,8}$");

        [ConsoleCommand("bunker.say")]
        private void SayCommand(ConsoleSystem.Arg arg)
        {
            // The server console and RCON have no connection; anyone else must be an admin.
            if (arg.Connection != null && arg.Connection.authLevel < 2)
            {
                arg.ReplyWith("Admins only.");
                return;
            }

            if (!arg.HasArgs(4))
            {
                arg.ReplyWith("Usage: bunker.say <steamid> <name> <colour> <message>");
                return;
            }

            ulong userId;
            if (!ulong.TryParse(arg.GetString(0), out userId)) userId = 0UL;

            var name = Clean(arg.GetString(1), 40);
            var colour = arg.GetString(2);
            if (!Colour.IsMatch(colour)) colour = "#5af";
            var message = Clean(arg.GetString(3), 300);

            if (name.Length == 0) name = "Admin";
            if (message.Length == 0)
            {
                arg.ReplyWith("The message is empty.");
                return;
            }

            // The same client command the game uses for a player's own chat: channel 0 (global), the Steam ID for the avatar,
            // the text, the name and its colour.
            ConsoleNetwork.BroadcastToAllClients("chat.add2", 0, userId, message, name, colour, 1f);
            Puts("[CHAT] " + name + ": " + message);
            arg.ReplyWith("sent");
        }

        // Removes control characters and anything that could be read as formatting markup.
        private static string Clean(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder();
            foreach (var c in text)
            {
                if (char.IsControl(c) || c == '<' || c == '>') continue;
                sb.Append(c);
                if (sb.Length >= maxLength) break;
            }
            return sb.ToString().Trim();
        }
    }
}
