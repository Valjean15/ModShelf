using System.Globalization;
using System.Text;

using ModShelf.Platform.Releases;
using ModShelf.Testing;

using Velopack.Locators;
using Velopack.Sources;

namespace ModShelf.Platform.Tests.Releases;

/// <summary>
/// Which of ModShelf's GitHub releases an installed copy finds, GitHub's answers made up: the 0.9 releases looked at
/// pre-releases too, so they see 1.0.0, and from 1.0.0 on, only stable releases count.
/// </summary>
public sealed class GitHubReleasesTests : IDisposable
{
    /// <summary>
    /// The pack ID the pack scripts give ModShelf's packages.
    /// </summary>
    private const string PackId = "ModShelf.App";

    private readonly TempFolder _folder = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task A09CopySeesTheFirstStableRelease()
    {
        // As 0.9.0 to 0.9.5 shipped it: pre-releases too.
        FakeGitHub github = new(("1.0.0", false), ("0.9.5", true), ("0.9.4", true));
        GithubSource asShipped = new(AppReleaseSources.Repository, accessToken: null, prerelease: true, github);

        Assert.Equal("1.0.0", (await Installed(asShipped, "0.9.4").CheckAsync(Cancellation))?.Version);
        Assert.Equal("1.0.0", (await Installed(asShipped, "0.9.5").CheckAsync(Cancellation))?.Version);
    }

    [Fact]
    public async Task FromTheFirstStableReleaseOnlyStableOnesAreSeen()
    {
        FakeGitHub github = new(("1.1.0-beta.1", true), ("1.0.1", false), ("1.0.0", false), ("0.9.5", true));

        Assert.Equal("1.0.1", (await Installed(AppReleaseSources.For(null, github), "1.0.0").CheckAsync(Cancellation))?.Version);
    }

    [Fact]
    public async Task APreReleaseIsntOfferedToAStableCopy()
    {
        FakeGitHub github = new(("1.1.0-beta.1", true), ("1.0.0", false), ("0.9.5", true));

        Assert.Null(await Installed(AppReleaseSources.For(null, github), "1.0.0").CheckAsync(Cancellation));
    }

    private VelopackAppReleases Installed(IUpdateSource source, string version)
    {
        string packages = Path.Combine(_folder.Path, "packages-" + version);
        Directory.CreateDirectory(packages);
        return new VelopackAppReleases(source, new TestVelopackLocator(PackId, version, packages, null));
    }

    /// <summary>
    /// GitHub's API and downloads for ModShelf's repository, with the releases given, newest first, each with a feed for
    /// each system listing its full package.
    /// </summary>
    private sealed class FakeGitHub(params (string Version, bool Prerelease)[] releases) : IFileDownloader
    {
        private const string Downloads = AppReleaseSources.Repository + "/releases/download/";

        private static readonly string[] Channels = ["win", "linux", "osx"];

        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Assert.Equal("https://api.github.com/repos/Valjean15/ModShelf/releases?per_page=10&page=1", url);
            DateTimeOffset published = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);
            IEnumerable<string> listed = releases.Select((release, index) =>
                $$"""
                {
                  "name": "ModShelf {{release.Version}}",
                  "prerelease": {{(release.Prerelease ? "true" : "false")}},
                  "published_at": "{{published.AddDays(-index).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}}",
                  "assets": [{{string.Join(", ", Channels.Select(channel => Asset(release.Version, channel)))}}]
                }
                """);
            return Task.FromResult("[" + string.Join(", ", listed) + "]");
        }

        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Assert.StartsWith(Downloads + "v", url, StringComparison.Ordinal);
            string version = url[(Downloads.Length + 1)..url.IndexOf('/', Downloads.Length)];
            string feed = $$"""{"Assets":[{"PackageId":"{{PackId}}","Version":"{{version}}","Type":"Full","FileName":"{{PackId}}-{{version}}-full.nupkg","SHA256":"{{new string('0', 64)}}","Size":1}]}""";
            return Task.FromResult(Encoding.UTF8.GetBytes(feed));
        }

        public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
            => throw new InvalidOperationException("Only a check is made here.");

        private static string Asset(string version, string channel)
        {
            string name = $"releases.{channel}.json";
            return $$"""{ "name": "{{name}}", "url": "https://api.github.com/assets/{{version}}-{{channel}}", "browser_download_url": "{{Downloads}}v{{version}}/{{name}}", "content_type": "application/json" }""";
        }
    }
}
