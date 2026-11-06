using Velopack.Sources;

namespace ModShelf.Platform.Releases;

/// <summary>
/// Where ModShelf is released: its GitHub repository's stable releases, from 1.0.0 on; or, for testing, a folder or a
/// web address that holds Velopack's release feed.
/// </summary>
/// <remarks>
/// The 0.9 releases looked at pre-releases too, so they see 1.0.0 and the stable releases after it. GitHub gives the
/// newest 10 releases, and Velopack looks no further, so a stable release must be among them.
/// </remarks>
internal static class AppReleaseSources
{
    public const string Repository = "https://github.com/Valjean15/ModShelf";

    /// <param name="feed">A folder or an http(s) address in place of GitHub; null for GitHub.</param>
    /// <param name="downloader">What reads GitHub, for tests; null for Velopack's own.</param>
    public static IUpdateSource For(string? feed, IFileDownloader? downloader = null)
    {
        if (string.IsNullOrWhiteSpace(feed))
        {
            return new GithubSource(Repository, accessToken: null, prerelease: false, downloader);
        }

        return Uri.TryCreate(feed, UriKind.Absolute, out Uri? address) && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            ? new SimpleWebSource(address)
            : new SimpleFileSource(new DirectoryInfo(Path.GetFullPath(feed)));
    }
}
