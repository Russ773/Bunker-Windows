using System.Drawing;
using Bunker;
using Xunit;

public class ChatTests
{
    [Fact]
    public void Chat_json_gives_name_message_colour_and_channel()
    {
        var (text, userId, name, message, color, channel, _) = Core.ParseChat("{\"Channel\":1,\"Message\":\"on my way\",\"UserId\":\"76561198000000001\",\"Username\":\"Ironclad\",\"Color\":\"#5af\",\"Time\":1700000000}");
        Assert.Equal("Ironclad: on my way", text);
        Assert.Equal("76561198000000001", userId);
        Assert.Equal("Ironclad", name);
        Assert.Equal("on my way", message);
        Assert.Equal("#5af", color);
        Assert.Equal(1, channel);
    }

    [Fact]
    public void Chat_without_colour_or_channel_still_works()
    {
        var (_, _, name, _, color, channel, _) = Core.ParseChat("{\"Username\":\"SERVER\",\"Message\":\"hello\"}");
        Assert.Equal("SERVER", name);
        Assert.Null(color);
        Assert.Equal(0, channel);
    }

    [Fact]
    public void Plain_text_is_not_treated_as_chat()
    {
        var (text, userId, name, _, _, _, _) = Core.ParseChat("just a line");
        Assert.Equal("just a line", text);
        Assert.Null(userId);
        Assert.Null(name);
    }

    [Theory]
    [InlineData("#5af", 0x55, 0xAA, 0xFF)]
    [InlineData("#55aaff", 0x55, 0xAA, 0xFF)]
    [InlineData("af5", 0xAA, 0xFF, 0x55)]
    public void Hex_colours_are_read(string text, int r, int g, int b)
    {
        var c = Theme.ParseHex(text);
        Assert.Equal(Color.FromArgb(r, g, b).ToArgb(), c!.Value.ToArgb());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("blue")]
    [InlineData("#12")]
    public void Not_a_colour_gives_null(string? text) => Assert.Null(Theme.ParseHex(text));

    [Fact]
    public void Very_dark_names_are_lightened_so_they_can_be_read_on_the_dark_theme()
    {
        var shown = Theme.Readable(Color.FromArgb(10, 10, 40));
        Assert.True(shown.R + shown.G + shown.B > 10 + 10 + 40 + 100);
        Assert.Equal(Color.FromArgb(0x55, 0xAA, 0xFF).ToArgb(), Theme.Readable(Color.FromArgb(0x55, 0xAA, 0xFF)).ToArgb()); // already readable
    }
}

public class UpdaterTests
{
    [Theory]
    [InlineData("https://github.com/Russ773/Bunker-Windows/releases/tag/v1.0.5", "1.0.5")]
    [InlineData("https://github.com/Russ773/Bunker-Windows/releases/tag/V2.10.0", "2.10.0")]
    public void The_version_is_read_from_the_release_address(string address, string expected) =>
        Assert.Equal(Version.Parse(expected), Updater.VersionFromReleaseAddress(address));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://github.com/Russ773/Bunker-Windows/releases")]
    [InlineData("https://github.com/Russ773/Bunker-Windows/releases/tag/nightly")]
    public void Anything_else_is_not_a_version(string? address) => Assert.Null(Updater.VersionFromReleaseAddress(address));
}

public class ChatTidyTests
{
    [Fact]
    public void A_plugins_prefix_and_repeated_name_are_split_out_of_the_message()
    {
        // Better Chat sends "[ADMIN] Russ: Testing" as the message while the name is also sent separately.
        var (text, _, name, message, _, channel, tags) = Core.ParseChat("{\"Channel\":1,\"Message\":\"[ADMIN] Russ: Testing\",\"UserId\":\"76561197984187003\",\"Username\":\"Russ\",\"Color\":\"#af5\"}");
        Assert.Equal("Russ", name);
        Assert.Equal("Testing", message);
        Assert.Equal("[ADMIN]", tags);
        Assert.Equal("[ADMIN] Russ: Testing", text);
        Assert.Equal(1, channel);
    }

    [Theory]
    [InlineData("Russ", "[ADMIN] [VIP] Russ: hello", "[ADMIN] [VIP]", "hello")]
    [InlineData("Russ", "[ADMIN] russ: hello", "[ADMIN]", "hello")]
    [InlineData("Russ", "[Team] Russ: a: b", "[Team]", "a: b")]
    public void Tags_before_the_players_own_name_are_separated(string user, string message, string tags, string rest)
    {
        var (gotTags, gotMessage) = ChatText.SplitTags(user, message);
        Assert.Equal(tags, gotTags);
        Assert.Equal(rest, gotMessage);
    }

    [Theory]
    [InlineData("Russ", "Russ: hello")]                  // no [tag]: could be the player typing their own name
    [InlineData("Russ", "[ADMIN] Someone: hello")]       // a different name
    [InlineData("Russ", "just chatting [lol] Russ: hi")] // tags must come first
    [InlineData("", "[ADMIN] Russ: hello")]
    public void Anything_else_is_left_alone(string user, string message)
    {
        var (tags, rest) = ChatText.SplitTags(user, message);
        Assert.Equal("", tags);
        Assert.Equal(message, rest);
    }

    [Fact]
    public void Colour_codes_and_control_characters_are_removed_from_chat()
    {
        Assert.Equal("[ADMIN] Russ: hi", ChatText.StripMarkup("<color=#af5>[ADMIN]</color> Russ: hi\u0007"));
        var (_, _, _, message, _, _, _) = Core.ParseChat("{\"Username\":\"Russ\",\"Message\":\"<size=20>big</size>\"}");
        Assert.Equal("big", message);
    }
}

public class WidgetUpdateTests
{
    [Theory]
    [InlineData("1.0.2", "1.0.0", true)]
    [InlineData("1.0.10", "1.0.9", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.2", false)]
    [InlineData(null, "1.0.0", false)]
    [InlineData("1.0.1", null, false)]
    [InlineData("soon", "1.0.0", false)]
    public void A_higher_version_counts_as_an_update(string? offered, string? installed, bool expected) =>
        Assert.Equal(expected, WidgetUpdateService.IsNewer(offered, installed));
}

public class FavouriteWidgetTests
{
    private static WidgetInfo W(string key) => new(key, key, "", WidgetOrigin.BuiltIn, WidgetKind.Announce);

    [Fact]
    public void Starred_widgets_come_first_and_keep_their_own_order()
    {
        var list = new[] { W("a"), W("b"), W("c"), W("d") };
        var ordered = WidgetCatalog.FavouritesFirst(list, new List<string> { "d", "b" });
        Assert.Equal(new[] { "b", "d", "a", "c" }, ordered.Select(w => w.Key));
    }

    [Fact]
    public void With_no_favourites_the_order_is_unchanged()
    {
        var list = new[] { W("a"), W("b") };
        Assert.Equal(new[] { "a", "b" }, WidgetCatalog.FavouritesFirst(list, new List<string>()).Select(w => w.Key));
    }

    [Fact]
    public void A_favourite_that_no_longer_exists_is_ignored()
    {
        var list = new[] { W("a"), W("b") };
        Assert.Equal(new[] { "a", "b" }, WidgetCatalog.FavouritesFirst(list, new List<string> { "gone" }).Select(w => w.Key));
    }
}

public class CommandGuessTests
{
    [Theory]
    [InlineData("status")]
    [InlineData("Status")]
    [InlineData("kick 76561198000000001 \"bye\"")]
    [InlineData("say hello everyone")]
    [InlineData("c.reload Economics")]
    [InlineData("oxide.plugins")]
    [InlineData("server.save")]
    [InlineData("inventory.giveto 765 wood 10")]
    [InlineData("count")]
    [InlineData("")]
    public void Commands_are_recognised(string text) => Assert.True(CommandGuess.LooksLikeCommand(text));

    [Theory]
    [InlineData("Hello")]
    [InlineData("hello there everyone")]
    [InlineData("stauts")]
    [InlineData("/home")]
    [InlineData("Is anyone online?")]
    public void Ordinary_words_are_not_commands(string text) => Assert.False(CommandGuess.LooksLikeCommand(text));

    [Fact]
    public void A_word_confirmed_as_a_command_is_not_asked_about_again()
    {
        var confirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rb" };
        Assert.False(CommandGuess.LooksLikeCommand("rb reload"));
        Assert.True(CommandGuess.LooksLikeCommand("rb reload", confirmed));
        Assert.True(CommandGuess.LooksLikeCommand("RB reload", confirmed));
    }

    [Fact]
    public void The_first_word_is_what_is_remembered() => Assert.Equal("rb", CommandGuess.FirstWord("  rb reload now"));
}
