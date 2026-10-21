using Bunker;
using Xunit;

public class SteamLookupTests
{
    private const string Id = "76561198000000001";

    // Made-up profile in the layout Steam serves with ?xml=1.
    private const string PublicProfile = """
    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
    <profile>
    <steamID64>76561198000000001</steamID64>
    <steamID><![CDATA[Ironclad]]></steamID>
    <onlineState>offline</onlineState>
    <privacyState>public</privacyState>
    <avatarIcon><![CDATA[https://avatars.fastly.steamstatic.com/aaa.jpg]]></avatarIcon>
    <avatarFull><![CDATA[https://avatars.fastly.steamstatic.com/aaa_full.jpg]]></avatarFull>
    <vacBanned>0</vacBanned>
    <tradeBanState>None</tradeBanState>
    <isLimitedAccount>0</isLimitedAccount>
    <memberSince>March 3, 2014</memberSince>
    <location>Netherlands</location>
    </profile>
    """;

    private const string FriendsOnlyProfile = """
    <profile>
    <steamID64>76561198000000002</steamID64>
    <steamID><![CDATA[Shadow]]></steamID>
    <privacyState>friendsonly</privacyState>
    <avatarMedium><![CDATA[https://avatars.fastly.steamstatic.com/bbb_medium.jpg]]></avatarMedium>
    <vacBanned>1</vacBanned>
    <tradeBanState>Banned</tradeBanState>
    <isLimitedAccount>1</isLimitedAccount>
    </profile>
    """;

    [Fact]
    public void A_public_profile_is_read()
    {
        var p = SteamLookup.ParseProfile(PublicProfile, Id)!;
        Assert.Equal("Ironclad", p.PersonaName);
        Assert.True(p.IsPublic);
        Assert.Equal("Public", SteamLookup.PrivacyText(p));
        Assert.False(p.VacBanned);
        Assert.False(p.Limited);
        Assert.Equal("March 3, 2014", p.MemberSince);
        Assert.Equal("Netherlands", p.Location);
        Assert.Equal("https://avatars.fastly.steamstatic.com/aaa_full.jpg", p.AvatarUrl);
        Assert.False(p.HasKeyData);
    }

    [Fact]
    public void A_profile_that_is_not_public_gives_what_little_there_is()
    {
        var p = SteamLookup.ParseProfile(FriendsOnlyProfile, "76561198000000002")!;
        Assert.Equal("Shadow", p.PersonaName);
        Assert.False(p.IsPublic);
        Assert.Equal("Friends only", SteamLookup.PrivacyText(p));
        Assert.True(p.VacBanned);
        Assert.True(p.Limited);
        Assert.Null(p.MemberSince);
        Assert.Equal("https://avatars.fastly.steamstatic.com/bbb_medium.jpg", p.AvatarUrl);
        Assert.Equal("Hidden", SteamLookup.AccountAge(p));
        Assert.Equal("Private", SteamLookup.PrivacyText(SteamLookup.ParseProfile(FriendsOnlyProfile.Replace("friendsonly", "private"), Id)!));
    }

    [Theory]
    [InlineData("<response><error><![CDATA[The specified profile could not be found.]]></error></response>")]
    [InlineData("<profile></profile>")]
    [InlineData("<profile><steamID></steamID></profile>")]
    [InlineData("not xml at all")]
    [InlineData("")]
    [InlineData("<!DOCTYPE profile [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]><profile><steamID>&x;</steamID></profile>")]
    public void Anything_that_is_not_a_profile_gives_nothing(string xml) => Assert.Null(SteamLookup.ParseProfile(xml, Id));

    // Made-up answers in the layout of the Steam Web API.
    private const string Summaries = """{"response":{"players":[{"steamid":"76561198000000001","personaname":"Ironclad","timecreated":1393804800,"loccountrycode":"NL"}]}}""";
    private const string NoBans = """{"players":[{"SteamId":"76561198000000001","CommunityBanned":false,"VACBanned":false,"NumberOfVACBans":0,"DaysSinceLastBan":0,"NumberOfGameBans":0,"EconomyBan":"none"}]}""";
    private const string Games = """{"response":{"game_count":1,"games":[{"appid":252490,"playtime_forever":131040,"playtime_2weeks":2184}]}}""";

    [Fact]
    public void With_a_key_the_hours_account_age_and_bans_are_added()
    {
        var p = SteamLookup.ApplyKeyData(SteamLookup.ParseProfile(PublicProfile, Id)!, Summaries, NoBans, Games);
        Assert.True(p.HasKeyData);
        Assert.Equal(2184, p.RustHours!.Value, 3);       // 131,040 minutes
        Assert.Equal(36.4, p.RustHoursLast2Weeks!.Value, 1);
        Assert.Equal(new DateTime(2014, 3, 3, 0, 0, 0, DateTimeKind.Utc), p.AccountCreated);
        Assert.Equal("NL", p.CountryCode);
        Assert.Equal("2,184 hours  (36 in last 2 weeks)", SteamLookup.RustLine(p, haveKey: true));
        Assert.Equal(("None on record", false), SteamLookup.Bans(p));
        Assert.StartsWith("3 Mar 2014  (", SteamLookup.AccountAge(p));
    }

    [Fact]
    public void Bans_from_the_key_are_put_in_plain_words_with_how_long_ago()
    {
        var bans = """{"players":[{"CommunityBanned":true,"VACBanned":true,"NumberOfVACBans":2,"DaysSinceLastBan":410,"NumberOfGameBans":1,"EconomyBan":"banned"}]}""";
        var p = SteamLookup.ApplyKeyData(SteamLookup.ParseProfile(PublicProfile, Id)!, Summaries, bans, Games);
        var (text, bad) = SteamLookup.Bans(p);
        Assert.True(bad);
        Assert.Contains("2 VAC bans", text);
        Assert.Contains("Game ban", text);
        Assert.Contains("Community ban", text);
        Assert.Contains("Trade ban: banned", text);
        Assert.Contains("(410 days ago)", text);
    }

    [Fact]
    public void A_game_list_the_player_keeps_private_is_noted_not_guessed()
    {
        var p = SteamLookup.ApplyKeyData(SteamLookup.ParseProfile(PublicProfile, Id)!, Summaries, NoBans, """{"response":{}}""");
        Assert.True(p.GamesHidden);
        Assert.Null(p.RustHours);
        Assert.StartsWith("Hidden", SteamLookup.RustLine(p, haveKey: true));
    }

    [Fact]
    public void A_player_who_does_not_own_Rust_has_no_hours_but_is_not_called_hidden()
    {
        var p = SteamLookup.ApplyKeyData(SteamLookup.ParseProfile(PublicProfile, Id)!, Summaries, NoBans, """{"response":{"game_count":0}}""");
        Assert.False(p.GamesHidden);
        Assert.Null(p.RustHours);
        Assert.Equal("None on record", SteamLookup.RustLine(p, haveKey: true));
    }

    [Fact]
    public void Without_a_key_the_card_says_what_a_key_adds()
    {
        var p = SteamLookup.ParseProfile(PublicProfile, Id)!;
        Assert.Equal("Needs a Steam key (Settings)", SteamLookup.RustLine(p, haveKey: false));
        Assert.Equal("March 3, 2014", SteamLookup.AccountAge(p));
        Assert.Equal(("None on record", false), SteamLookup.Bans(p));
    }

    [Fact]
    public void Garbled_key_answers_are_ignored_and_the_public_profile_still_shows()
    {
        var p = SteamLookup.ApplyKeyData(SteamLookup.ParseProfile(PublicProfile, Id)!, "oops", "{", "<html>");
        Assert.True(p.HasKeyData);
        Assert.Equal("Ironclad", p.PersonaName);
        Assert.Null(p.RustHours);
    }

    [Fact]
    public void The_public_page_alone_can_show_a_VAC_ban()
    {
        var (text, bad) = SteamLookup.Bans(SteamLookup.ParseProfile(FriendsOnlyProfile, "76561198000000002")!);
        Assert.True(bad);
        Assert.Contains("VAC ban", text);
        Assert.Contains("Trade ban: Banned", text);
    }

    [Theory]
    [InlineData(0, "under a month old")]
    [InlineData(45, "1 month old")]
    [InlineData(200, "6 months old")]
    [InlineData(400, "1 year old")]
    [InlineData(4000, "10 years old")]
    public void Account_age_is_in_plain_words(int days, string expected) => Assert.Equal(expected, SteamLookup.YearsText(TimeSpan.FromDays(days)));

    [Theory]
    [InlineData("76561198000000001", true)]
    [InlineData("7656119800000000", false)]
    [InlineData("765611980000000011", false)]
    [InlineData("7656119800000000x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_real_looking_Steam_ID_is_ever_looked_up(string? id, bool expected) => Assert.Equal(expected, SteamLookup.IsSteamId(id));

    [Theory]
    [InlineData("0123456789ABCDEF0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF0123456789abcde", false)]
    [InlineData("0123456789ABCDEF0123456789abcdeg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_Steam_key_is_32_hex_digits(string? key, bool expected) => Assert.Equal(expected, SteamLookup.LooksLikeKey(key));

    [Theory]
    [InlineData("https://avatars.steamstatic.com/abc_full.jpg", true)]
    [InlineData("https://avatars.fastly.steamstatic.com/abc_full.jpg", true)]
    [InlineData("https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/ab/abc.jpg", true)]
    [InlineData("http://avatars.steamstatic.com/abc.jpg", false)]
    [InlineData("https://evil.example.com/abc.jpg", false)]
    [InlineData("https://steamstatic.com.evil.example/abc.jpg", false)]
    [InlineData("file:///c:/secret.txt", false)]
    [InlineData("not a url", false)]
    public void Pictures_are_only_fetched_from_Steams_own_servers(string url, bool expected) => Assert.Equal(expected, SteamLookup.IsAvatarAddress(url));

    [Fact]
    public void A_card_asks_Steam_by_default_and_has_no_key_until_you_give_one()
    {
        var s = new AppSettings();
        Assert.True(s.SteamLookups);
        Assert.Equal("", s.SteamApiKey);
        Assert.Null(s.SteamApiKeyProtected);
    }

    [Fact]
    public void The_key_is_stored_encrypted_and_comes_back()
    {
        var s = new AppSettings { SteamApiKey = "0123456789ABCDEF0123456789abcdef" };
        Assert.NotNull(s.SteamApiKeyProtected);
        Assert.DoesNotContain("0123456789ABCDEF", s.SteamApiKeyProtected);
        Assert.Equal("0123456789ABCDEF0123456789abcdef", s.SteamApiKey);
        s.SteamApiKey = "";
        Assert.Null(s.SteamApiKeyProtected);
    }
}
