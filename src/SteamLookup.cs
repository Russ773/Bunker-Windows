using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Bunker;

/// <summary>What Steam says about a player. Anything a private profile hides, or that needs a Steam key, is null.</summary>
public sealed class SteamProfile
{
    public string SteamId { get; init; } = "";
    public string PersonaName { get; init; } = "";
    /// <summary>"public", "friendsonly" or "private".</summary>
    public string Privacy { get; init; } = "private";
    public bool IsPublic => Privacy == "public";
    public bool VacBanned { get; init; }
    public string TradeBan { get; init; } = "None";
    public bool Limited { get; init; }
    public string? MemberSince { get; init; }
    public string? Location { get; init; }
    public string? AvatarUrl { get; init; }

    // From the Steam Web API, when you have given Bunker a (free) key.
    public bool HasKeyData { get; init; }
    public DateTime? AccountCreated { get; init; }
    public string? CountryCode { get; init; }
    public double? RustHours { get; init; }
    public double? RustHoursLast2Weeks { get; init; }
    /// <summary>With a key: the player keeps their game list private, so there is no number to show.</summary>
    public bool GamesHidden { get; init; }
    public int VacBans { get; init; }
    public int GameBans { get; init; }
    public int? DaysSinceLastBan { get; init; }
    public bool CommunityBanned { get; init; }
}

/// <summary>A profile with its picture, or the reason there is none.</summary>
public sealed record SteamResult(SteamProfile? Profile, byte[]? Avatar, string Message);

/// <summary>
/// Looks a player up on Steam. The public community profile needs no key. With a free Steam Web API key (optional, set in Settings) Bunker
/// also gets their hours in Rust, how old the account is and the full ban record. Only the player's Steam ID (and the key, to Steam) is sent,
/// and only when a player card is opened. Answers are kept for a few minutes so reopening a card is instant.
/// </summary>
public static class SteamLookup
{
    public const int RustAppId = 252490;
    private const int MaxBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(10);
    private static readonly HttpClient Http = Create();
    private static readonly Dictionary<string, (DateTime At, SteamResult Result)> Cache = new();

    private static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Bunker-PlayerCard");
        return c;
    }

    /// <summary>A Steam ID is 17 digits. Anything else is never sent anywhere.</summary>
    public static bool IsSteamId(string? id) => id is { Length: 17 } && id.All(char.IsDigit);

    /// <summary>A Steam Web API key is 32 hex digits.</summary>
    public static bool LooksLikeKey(string? key) => key is { Length: 32 } && key.All(Uri.IsHexDigit);

    public static async Task<SteamResult> GetAsync(string steamId, string? apiKey, CancellationToken ct = default)
    {
        if (!IsSteamId(steamId)) return new SteamResult(null, null, "This player has no Steam ID to look up.");
#if DEBUG
        if (Environment.GetEnvironmentVariable("BUNKER_TEST_STEAMFAKE") is { Length: > 0 } mode) return Fake(steamId, mode == "key");
#endif
        var useKey = LooksLikeKey(apiKey);
        var cacheKey = steamId + (useKey ? "+key" : "");
        lock (Cache)
            if (Cache.TryGetValue(cacheKey, out var hit) && DateTime.UtcNow - hit.At < KeepFor) return hit.Result;

        SteamResult result;
        try
        {
            var xml = await GetTextAsync($"https://steamcommunity.com/profiles/{steamId}/?xml=1", ct);
            var profile = ParseProfile(xml, steamId);
            var note = "";
            if (profile is null) result = new SteamResult(null, null, "Steam has no public profile for this player.");
            else
            {
                if (useKey)
                {
                    try { profile = await WithKeyDataAsync(profile, apiKey!, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (HttpRequestException e) when (e.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
                    { note = "Steam did not accept your Steam key, so only the public profile is shown. Check it in Settings → General."; }
                    catch { note = "Steam's key lookups did not answer, so only the public profile is shown."; }
                }
                byte[]? avatar = null;
                if (profile.AvatarUrl is { } url && IsAvatarAddress(url))
                {
                    try { avatar = await GetBytesAsync(url, 600 * 1024, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch { }
                }
                result = new SteamResult(profile, avatar, note);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { return new SteamResult(null, null, "Could not reach Steam: " + e.Message); } // not kept, so the next try asks again

        lock (Cache) Cache[cacheKey] = (DateTime.UtcNow, result);
        return result;
    }

    private static async Task<SteamProfile> WithKeyDataAsync(SteamProfile profile, string key, CancellationToken ct)
    {
        var id = profile.SteamId;
        var k = Uri.EscapeDataString(key);
        var summaries = GetTextAsync($"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={k}&steamids={id}", ct);
        var bans = GetTextAsync($"https://api.steampowered.com/ISteamUser/GetPlayerBans/v1/?key={k}&steamids={id}", ct);
        var games = GetTextAsync($"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={k}&steamid={id}&include_played_free_games=1&appids_filter%5B0%5D={RustAppId}", ct);
        await Task.WhenAll(summaries, bans, games);
        return ApplyKeyData(profile, summaries.Result, bans.Result, games.Result);
    }

    /// <summary>Adds what the three Steam Web API answers say to a profile read from the community page.</summary>
    public static SteamProfile ApplyKeyData(SteamProfile p, string summariesJson, string bansJson, string gamesJson)
    {
        DateTime? created = null;
        string? country = null;
        int vac = 0, gameBans = 0;
        int? days = null;
        bool community = false, vacBanned = p.VacBanned;
        string trade = p.TradeBan;
        double? hours = null, twoWeeks = null;
        var hidden = false;

        try
        {
            using var doc = JsonDocument.Parse(summariesJson);
            if (doc.RootElement.GetProperty("response").GetProperty("players").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } player)
            {
                if (player.TryGetProperty("timecreated", out var t) && t.TryGetInt64(out var seconds)) created = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                if (player.TryGetProperty("loccountrycode", out var c)) country = c.GetString();
            }
        }
        catch { }

        try
        {
            using var doc = JsonDocument.Parse(bansJson);
            if (doc.RootElement.GetProperty("players").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } b)
            {
                community = b.TryGetProperty("CommunityBanned", out var cb) && cb.ValueKind == JsonValueKind.True;
                vacBanned = b.TryGetProperty("VACBanned", out var vb) ? vb.ValueKind == JsonValueKind.True : vacBanned;
                vac = b.TryGetProperty("NumberOfVACBans", out var nv) && nv.TryGetInt32(out var nvi) ? nvi : 0;
                gameBans = b.TryGetProperty("NumberOfGameBans", out var ng) && ng.TryGetInt32(out var ngi) ? ngi : 0;
                days = b.TryGetProperty("DaysSinceLastBan", out var d) && d.TryGetInt32(out var di) ? di : null;
                if (b.TryGetProperty("EconomyBan", out var eb) && eb.GetString() is { Length: > 0 } econ) trade = econ;
            }
        }
        catch { }

        try
        {
            using var doc = JsonDocument.Parse(gamesJson);
            var response = doc.RootElement.GetProperty("response");
            // Steam answers with an empty response when the player keeps their game list private.
            if (!response.TryGetProperty("game_count", out _)) hidden = true;
            else if (response.TryGetProperty("games", out var list))
                foreach (var g in list.EnumerateArray().Where(g => g.TryGetProperty("appid", out var a) && a.TryGetInt32(out var ai) && ai == RustAppId))
                {
                    if (g.TryGetProperty("playtime_forever", out var pf) && pf.TryGetInt64(out var minutes)) hours = minutes / 60.0;
                    if (g.TryGetProperty("playtime_2weeks", out var p2) && p2.TryGetInt64(out var minutes2)) twoWeeks = minutes2 / 60.0;
                }
        }
        catch { }

        return new SteamProfile
        {
            SteamId = p.SteamId, PersonaName = p.PersonaName, Privacy = p.Privacy, VacBanned = vacBanned, TradeBan = trade, Limited = p.Limited,
            MemberSince = p.MemberSince, Location = p.Location, AvatarUrl = p.AvatarUrl, HasKeyData = true,
            AccountCreated = created, CountryCode = country, RustHours = hours, RustHoursLast2Weeks = twoWeeks, GamesHidden = hidden,
            VacBans = vac, GameBans = gameBans, DaysSinceLastBan = days, CommunityBanned = community,
        };
    }

    private static async Task<string> GetTextAsync(string url, CancellationToken ct) =>
        System.Text.Encoding.UTF8.GetString(await GetBytesAsync(url, MaxBytes, ct));

    private static async Task<byte[]> GetBytesAsync(string url, int limit, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > limit) throw new InvalidOperationException("The reply was too large.");
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > limit) throw new InvalidOperationException("The reply was too large.");
        }
        return ms.ToArray();
    }

    /// <summary>Only pictures from Steam's own picture servers are fetched, whatever the profile says.</summary>
    public static bool IsAvatarAddress(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".akamaihd.net", StringComparison.OrdinalIgnoreCase));

    private static readonly XmlReaderSettings Safe = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes };

    /// <summary>Reads the profile page Steam serves with ?xml=1. Returns null for a profile that does not exist or text that is not a profile.</summary>
    public static SteamProfile? ParseProfile(string xml, string steamId)
    {
        XElement? root;
        try { using var reader = XmlReader.Create(new StringReader(xml), Safe); root = XDocument.Load(reader).Root; }
        catch { return null; }
        if (root is not { Name.LocalName: "profile" }) return null;
        string? Text(string name) => root.Element(name)?.Value.Trim() is { Length: > 0 } v ? v : null;

        var persona = Text("steamID");
        if (persona is null) return null;
        return new SteamProfile
        {
            SteamId = Text("steamID64") ?? steamId, PersonaName = persona, Privacy = Text("privacyState") ?? "private",
            VacBanned = Text("vacBanned") == "1", TradeBan = Text("tradeBanState") ?? "None", Limited = Text("isLimitedAccount") == "1",
            MemberSince = Text("memberSince"), Location = Text("location"),
            AvatarUrl = Text("avatarFull") ?? Text("avatarMedium") ?? Text("avatarIcon"),
        };
    }

    // ---------------------------------------------------------------- the words on the card

    /// <summary>"Public", "Friends only" or "Private".</summary>
    public static string PrivacyText(SteamProfile p) => p.Privacy switch { "public" => "Public", "friendsonly" => "Friends only", _ => "Private" };

    /// <summary>The bans line: what Steam says, in plain words, and whether it is bad news.</summary>
    public static (string Text, bool Bad) Bans(SteamProfile p)
    {
        var parts = new List<string>();
        if (p.VacBanned || p.VacBans > 0) parts.Add(p.VacBans > 1 ? $"{p.VacBans} VAC bans" : "VAC ban");
        if (p.GameBans > 0) parts.Add(p.GameBans > 1 ? $"{p.GameBans} game bans" : "Game ban");
        if (p.CommunityBanned) parts.Add("Community ban");
        if (!string.IsNullOrWhiteSpace(p.TradeBan) && !string.Equals(p.TradeBan, "None", StringComparison.OrdinalIgnoreCase)) parts.Add("Trade ban: " + p.TradeBan);
        if (parts.Count == 0) return ("None on record", false);
        var text = string.Join(", ", parts);
        if (p.DaysSinceLastBan is { } d && (p.VacBanned || p.GameBans > 0 || p.VacBans > 0)) text += $" ({d:N0} days ago)";
        return (text, true);
    }

    /// <summary>Hours in Rust as a card line, or why there are none.</summary>
    public static string RustLine(SteamProfile p, bool haveKey)
    {
        if (p.RustHours is { } h)
            return $"{h:N0} hours" + (p.RustHoursLast2Weeks is { } w && w >= 0.5 ? $"  ({w:N0} in last 2 weeks)" : "");
        if (!haveKey) return "Needs a Steam key (Settings)";
        if (p.GamesHidden) return "Hidden (the player keeps this private)";
        return "None on record";
    }

    /// <summary>When the Steam account was made: the exact date with a key, or the date Steam's public page shows.</summary>
    public static string AccountAge(SteamProfile p) =>
        p.AccountCreated is { } d ? d.ToString("d MMM yyyy", CultureInfo.CurrentCulture) + $"  ({YearsText(DateTime.UtcNow - d)})"
        : p.MemberSince ?? "Hidden";

    public static string YearsText(TimeSpan age)
    {
        var years = (int)(age.TotalDays / 365.25);
        if (years >= 1) return years == 1 ? "1 year old" : $"{years} years old";
        var months = (int)(age.TotalDays / 30.44);
        return months >= 1 ? (months == 1 ? "1 month old" : $"{months} months old") : "under a month old";
    }

#if DEBUG
    // Test hook (debug builds only): a made-up profile, so the card can be photographed without looking anyone up.
    private static SteamResult Fake(string id, bool withKey) => new(new SteamProfile
    {
        SteamId = id, PersonaName = "Ironclad", Privacy = "public", MemberSince = "March 3, 2014", Location = "Netherlands",
        HasKeyData = withKey, AccountCreated = withKey ? new DateTime(2014, 3, 3, 0, 0, 0, DateTimeKind.Utc) : null,
        RustHours = withKey ? 2184 : null, RustHoursLast2Weeks = withKey ? 36 : null,
    }, null, "");
#endif
}
