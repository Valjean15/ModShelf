using Velopack.Sources;

namespace ModShelf.Platform.Releases;

/// <summary>
/// Where ModShelf is released: its GitHub repository's releases, pre-releases too while it's in beta; or, for testing,
/// a folder or a web address that holds Velopack's release feed.
/// </summary>
internal static class AppReleaseSources
{
    public const string Repository = "https://github.com/Valjean15/ModShelf";

    /// <param name="feed">A folder or an http(s) address in place of GitHub; null for GitHub.</param>
    public static IUpdateSource For(string? feed)
    {
        if (string.IsNullOrWhiteSpace(feed))
        {
            return new GithubSource(Repository, accessToken: null, prerelease: true);
        }

        return Uri.TryCreate(feed, UriKind.Absolute, out Uri? address) && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            ? new SimpleWebSource(address)
            : new SimpleFileSource(new DirectoryInfo(Path.GetFullPath(feed)));
    }
}
