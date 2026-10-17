using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Bunker;

public record UpdateInfo(Version Version, string DownloadUrl, string Notes);

/// <summary>Checks GitHub Releases for a newer installer and runs it silently.</summary>
public static class Updater
{
    public const string Repo = "Russ773/Bunker-Windows";

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder? name);

    /// <summary>True when running as the Microsoft Store (MSIX) version, which the Store updates itself.</summary>
    public static bool IsStoreVersion { get; } = DetectPackaged();

    private static bool DetectPackaged()
    {
        try
        {
            int length = 0;
            return GetCurrentPackageFullName(ref length, null) != 15700; // APPMODEL_ERROR_NO_PACKAGE
        }
        catch (EntryPointNotFoundException) { return false; }
    }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Bunker-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>The build counter (4th part of the version); goes up with every build.</summary>
    public static int BuildNumber => Math.Max(Assembly.GetExecutingAssembly().GetName().Version?.Revision ?? 0, 0);

    /// <summary>For display, e.g. "v0.0.3  -  build 57".</summary>
    public static string VersionText => $"v{CurrentVersion}  ·  build {BuildNumber}";

    private static readonly HttpClient NoRedirect = CreateNoRedirectClient();

    private static HttpClient CreateNoRedirectClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Bunker-Updater");
        return c;
    }

    /// <summary>
    /// Returns the newer release, or null if already up to date. Throws on network/parse errors.
    /// It first reads the release page GitHub redirects "latest" to, which is not limited the way GitHub's API is (60 requests an hour),
    /// and only uses the API if that fails.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try { return await CheckViaReleasePageAsync(ct); }
        catch (OperationCanceledException) { throw; }
        catch { /* fall back to the API below */ }
        return await CheckViaApiAsync(ct);
    }

    /// <summary>The version in the address GitHub redirects /releases/latest to (…/releases/tag/v1.0.5), or null.</summary>
    public static Version? VersionFromReleaseAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !address.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase)) return null;
        var tag = address[(address.LastIndexOf('/') + 1)..].TrimStart('v', 'V');
        return Version.TryParse(tag, out var v) ? v : null;
    }

    private static async Task<UpdateInfo?> CheckViaReleasePageAsync(CancellationToken ct)
    {
        using var resp = await NoRedirect.GetAsync($"https://github.com/{Repo}/releases/latest", HttpCompletionOption.ResponseHeadersRead, ct);
        var latest = VersionFromReleaseAddress(resp.Headers.Location?.ToString())
            ?? throw new InvalidOperationException("The release page did not name a version.");
        if (latest <= CurrentVersion) return null;
        var tag = latest.ToString();
        return new UpdateInfo(latest, $"https://github.com/{Repo}/releases/download/v{tag}/Bunker-Setup-{tag}.exe", "");
    }

    private static async Task<UpdateInfo?> CheckViaApiAsync(CancellationToken ct)
    {
        string json;
        try { json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct); }
        catch (HttpRequestException e) when (e.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException("GitHub is limiting update checks from your internet connection for a while (up to an hour). Please try again later.");
        }
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString()!.TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var latest) || latest <= CurrentVersion) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            {
                var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                return new UpdateInfo(latest, asset.GetProperty("browser_download_url").GetString()!, notes);
            }
        }
        throw new InvalidOperationException($"Release v{latest} has no installer attached.");
    }

    /// <summary>Downloads the installer and launches it silently. The caller should exit the app afterwards.</summary>
    public static async Task DownloadAndLaunchAsync(UpdateInfo update, IProgress<int>? progress = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Bunker-Setup-{update.Version}.exe");
        using (var resp = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(path);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress?.Report((int)(done * 100 / total));
            }
        }

        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
    }
}
