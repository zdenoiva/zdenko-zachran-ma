using System.Net.Http;
using System.Text.Json;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed class UpdateInfo
{
    public bool Available;
    public string Version = "";
    public string PortableUrl = "";
    public string InstallerUrl = "";
    public string PageUrl = "";
    public string Sha256 = "";
    public long Size;
    public string PublishedAt = "";
    public string Changelog = "";
}

public static class UpdateService
{
    public const string RepoOwner = "zdenoiva";
    public const string RepoName = "zdenko-zachran-ma";
    public const string FeedUrl =
        "https://raw.githubusercontent.com/zdenoiva/zdenko-zachran-ma/main/latest.json";
    public const string PageUrl =
        "https://zdenoiva.github.io/zdenko-zachran-ma/";
    public const string ReleasesUrl =
        "https://github.com/zdenoiva/zdenko-zachran-ma/releases";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"ZachranaDisku/{AppInfo.VersionDisplay} (+{PageUrl})");
        return client;
    }

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var url = FeedUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            return Parse(json);
        }
        catch
        {
            return null;
        }
    }

    internal static UpdateInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var version = Str(root, "version");
            if (string.IsNullOrWhiteSpace(version)) return null;

            var info = new UpdateInfo
            {
                Version = version,
                PortableUrl = Str(root, "portableUrl"),
                InstallerUrl = Str(root, "installerUrl"),
                PageUrl = Str(root, "pageUrl"),
                Sha256 = Str(root, "sha256"),
                Size = Long(root, "size"),
                PublishedAt = Str(root, "publishedAt"),
                Changelog = Changelog(root)
            };
            info.Available = IsNewer(info.Version, AppInfo.Version);
            return info;
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsNewer(string latest, string current)
        => ParseVersion(latest) > ParseVersion(current);

    internal static Version ParseVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new Version(0, 0, 0);
        var text = value.Trim().TrimStart('v', 'V');
        var dash = text.IndexOf('-');
        if (dash > 0) text = text[..dash];
        if (!Version.TryParse(text, out var version)) return new Version(0, 0, 0);
        return new Version(version.Major, version.Minor, version.Build >= 0 ? version.Build : 0);
    }

    private static string Str(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => ""
        };
    }

    private static long Long(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), out var parsed)) return parsed;
        return 0;
    }

    private static string Changelog(JsonElement root)
    {
        if (!root.TryGetProperty("changelog", out var list) ||
            list.ValueKind != JsonValueKind.Array)
            return "";

        var lines = new List<string>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var line = item.GetString();
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line.Trim());
            }
        }
        return string.Join("\n", lines);
    }
}
