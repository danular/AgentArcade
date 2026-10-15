using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentArcade;

// Tells the user when a newer Agent Arcade is out. At most once a day, after start-up and off the UI thread, it asks
// GitHub's releases API for the newest release's tag. The request carries a generic User-Agent and nothing else: no
// version, no machine id, no cookies, no token. Only that JSON is read; a release file is never downloaded (nothing
// under releases/latest/download). A failure stays silent; the log says "update check failed" and nothing more.
// Settings: AppSettings.UpdateCheck (off in the sidebar: click the version), UpdateCheckedAt, UpdateTag, UpdateUrl.
static class UpdateCheck
{
    public const string Api = "https://api.github.com/repos/danular/AgentArcade/releases/latest";
    public const string Releases = "https://github.com/danular/AgentArcade/releases";
    const string UserAgent = "AgentArcade-update-check";
    static readonly TimeSpan Every = TimeSpan.FromDays(1);

    public sealed record Release(string Tag, string Url);

    // A day has passed since the last check, or the clock went back.
    public static bool Due(DateTimeOffset? checkedAt, DateTimeOffset now) => checkedAt is not { } at || now - at >= Every || at > now;

    // "v1.3.4" or "1.3.4" (two to four numbers) as a version; null for anything else, a pre-release like "v1.4.0-beta" too.
    public static Version? Parse(string? tag)
    {
        var match = Regex.Match(tag?.Trim() ?? "", @"^v?(\d{1,9})\.(\d{1,9})(?:\.(\d{1,9}))?(?:\.(\d{1,9}))?$");
        if (!match.Success) return null;
        int Part(int group) => match.Groups[group].Success ? int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;
        return new Version(Part(1), Part(2), Part(3), Part(4));
    }

    public static bool IsNewer(string? tag, string running) => Parse(tag) is { } latest && Parse(running) is { } current && latest > current;

    // What the notice shows: "1.3.4" for the tag "v1.3.4".
    public static string Shown(string tag) => tag.Trim().TrimStart('v');

    // The page the notice opens: html_url when it is one of this repository's release pages, else the list of releases.
    public static string PageFor(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
           && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.Query.Length == 0 && uri.Fragment.Length == 0
           && uri.AbsolutePath.StartsWith("/danular/AgentArcade/releases/tag/", StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : Releases;

    // The release in the API's answer, or null when it has no usable tag.
    public static Release? FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var tag = Json.Str(doc.RootElement, "tag_name").Trim();
        return Parse(tag) == null ? null : new Release(tag, PageFor(Json.Str(doc.RootElement, "html_url")));
    }

    // One GET of the releases API, 10 s at most. Throws or returns null when it fails.
    public static async Task<Release?> LatestAsync()
    {
        using var handler = new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1 << 20 };
        using var request = new HttpRequestMessage(HttpMethod.Get, Api);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? FromJson(await response.Content.ReadAsStringAsync()) : null;
    }
}
