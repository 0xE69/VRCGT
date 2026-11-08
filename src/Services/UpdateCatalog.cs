using System.Text.RegularExpressions;

namespace VRCGroupTools.Services;

public sealed record UpdateAsset(string Name, string Url);
public sealed record UpdateRelease(string Tag, bool Draft, bool Prerelease, IReadOnlyList<UpdateAsset> Assets);
public sealed record UpdateCandidate(string Version, string ZipUrl, string? ChecksumUrl);

public static class UpdateCatalog
{
    public static Version? ParseVersion(string? tag) => tag != null && Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$") && Version.TryParse(tag.TrimStart('v'), out var version) ? version : null;
    public static UpdateCandidate? Select(IEnumerable<UpdateRelease> releases, string current)
    {
        var installed = ParseVersion(current);
        if (installed == null) return null;
        foreach (var release in releases.Where(r => !r.Draft && !r.Prerelease).OrderByDescending(r => ParseVersion(r.Tag)))
        {
            var version = ParseVersion(release.Tag);
            if (version == null || version <= installed) continue;
            var expected = $"VRCGroupTools-v{version}-win-x64.zip";
            var zip = release.Assets.SingleOrDefault(a => a.Name == expected && SafeUrl(a.Url));
            if (zip == null) continue;
            var checksum = release.Assets.SingleOrDefault(a => a.Name == expected + ".sha256" && SafeUrl(a.Url));
            return new(version.ToString(), zip.Url, checksum?.Url);
        }
        return null;
    }
    private static bool SafeUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/0xE69/VRCGT/releases/download/", StringComparison.Ordinal) && string.IsNullOrEmpty(uri.UserInfo);
}
