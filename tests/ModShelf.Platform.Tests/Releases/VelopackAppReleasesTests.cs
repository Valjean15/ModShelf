using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using ModShelf.Application.Releases;
using ModShelf.Platform.Releases;
using ModShelf.Testing;

using Velopack.Locators;
using Velopack.Sources;

namespace ModShelf.Platform.Tests.Releases;

/// <summary>
/// Finding and downloading a newer ModShelf through Velopack, from a release feed in a folder, as an installed
/// ModShelf of version 0.9.0.
/// </summary>
public sealed class VelopackAppReleasesTests : IDisposable
{
    /// <summary>
    /// The pack ID the pack scripts give ModShelf's packages.
    /// </summary>
    private const string PackId = "ModShelf.App";

    private readonly TempFolder _folder = new();

    private string Feed => Path.Combine(_folder.Path, "feed");

    private string Packages => Path.Combine(_folder.Path, "packages");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task WithoutVelopacksStartUpNothingIsInstalled()
    {
        // As in the CLI, or a test run: Velopack's start-up code never ran.
        VelopackAppReleases releases = new(new SimpleFileSource(new DirectoryInfo(Feed)));

        Assert.False(releases.IsInstalled);
        Assert.Null(releases.Downloaded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => releases.CheckAsync(Cancellation));
        Assert.Throws<InvalidOperationException>(() => releases.RestartToApply(new AppRelease("0.9.1", null)));
    }

    [Fact]
    public async Task FindsANewerReleaseWithItsNotes()
    {
        Publish("0.9.1", "Fixed the update check.");

        Assert.Equal(new AppRelease("0.9.1", "Fixed the update check."), await Installed().CheckAsync(Cancellation));
    }

    [Fact]
    public async Task TheSameReleaseIsntNewer()
    {
        Publish("0.9.0", null);

        Assert.Null(await Installed().CheckAsync(Cancellation));
    }

    [Fact]
    public async Task DownloadsTheReleaseFoundCheckingItsWhole()
    {
        Publish("0.9.1", null);
        VelopackAppReleases releases = Installed();
        AppRelease found = (await releases.CheckAsync(Cancellation))!;
        List<int> progress = [];

        await releases.DownloadAsync(found, new SynchronousProgress(progress.Add), Cancellation);

        Assert.Equal(100, progress[^1]);
        Assert.True(File.Exists(Path.Combine(Packages, "ModShelf.App-0.9.1-full.nupkg")));
    }

    [Fact]
    public async Task ADownloadedReleaseWaitsForTheRestartIntoIt()
    {
        Publish("0.9.1", "Fixed the update check.");
        VelopackAppReleases releases = Installed();
        await releases.DownloadAsync((await releases.CheckAsync(Cancellation))!, null, Cancellation);

        Assert.Equal(new AppRelease("0.9.1", "Fixed the update check."), releases.Downloaded);
        Assert.Equal(new AppRelease("0.9.1", "Fixed the update check."), Installed().Downloaded);
        Assert.Throws<InvalidOperationException>(() => releases.RestartToApply(new AppRelease("0.9.5", null)));
    }

    [Fact]
    public async Task ADamagedReleaseIsRefused()
    {
        Publish("0.9.1", null, damaged: true);
        VelopackAppReleases releases = Installed();
        AppRelease found = (await releases.CheckAsync(Cancellation))!;

        await Assert.ThrowsAnyAsync<IOException>(() => releases.DownloadAsync(found, null, Cancellation));
    }

    [Fact]
    public async Task OnlyAReleaseACheckFoundIsDownloaded()
    {
        Publish("0.9.1", null);
        VelopackAppReleases releases = Installed();

        await Assert.ThrowsAsync<InvalidOperationException>(() => releases.DownloadAsync(new AppRelease("0.9.1", null), null, Cancellation));
        await releases.CheckAsync(Cancellation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => releases.DownloadAsync(new AppRelease("0.9.5", null), null, Cancellation));
    }

    [Fact]
    public async Task AFeedThatCantBeReadSaysSo()
    {
        Publish("0.9.1", null);
        foreach (string feed in Directory.GetFiles(Feed, "releases.*.json"))
        {
            File.WriteAllText(feed, "{ not a feed");
        }

        IOException refused = await Assert.ThrowsAnyAsync<IOException>(() => Installed().CheckAsync(Cancellation));
        Assert.StartsWith("ModShelf's releases couldn't be read", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyADownloadedReleaseIsApplied()
    {
        Assert.Throws<InvalidOperationException>(() => Installed().RestartToApply(new AppRelease("0.9.1", null)));
    }

    [Fact]
    public void ReleasesComeFromGitHubOrTheFeedGiven()
    {
        GithubSource github = Assert.IsType<GithubSource>(AppReleaseSources.For(null));
        Assert.Equal(new Uri("https://github.com/Valjean15/ModShelf"), github.RepoUri);
        Assert.True(github.Prerelease);
        Assert.IsType<SimpleWebSource>(AppReleaseSources.For("https://releases.example/modshelf/"));
        Assert.IsType<SimpleWebSource>(AppReleaseSources.For("http://localhost:8080/modshelf/"));
        SimpleFileSource folder = Assert.IsType<SimpleFileSource>(AppReleaseSources.For(Feed));
        Assert.Equal(Feed, folder.BaseDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar));
    }

    private VelopackAppReleases Installed()
    {
        Directory.CreateDirectory(Packages);
        return new VelopackAppReleases(new SimpleFileSource(new DirectoryInfo(Feed)), new TestVelopackLocator(PackId, "0.9.0", Packages, null));
    }

    /// <summary>
    /// A release in the feed, as vpk writes it: the package, a zip with the manifest that names its version, and a feed
    /// for each system listing it. Velopack checks a package by its SHA-256 when the feed has one, which vpk always
    /// writes.
    /// </summary>
    private void Publish(string version, string? notes, bool damaged = false)
    {
        Directory.CreateDirectory(Feed);
        byte[] package = Package(version, notes);
        string name = $"{PackId}-{version}-full.nupkg";
        File.WriteAllBytes(Path.Combine(Feed, name), damaged ? [.. package, 0] : package);
        string notesJson = notes is null ? string.Empty : $",\"NotesMarkdown\":\"{notes}\"";
        string feed = $$"""{"Assets":[{"PackageId":"{{PackId}}","Version":"{{version}}","Type":"Full","FileName":"{{name}}","SHA256":"{{Convert.ToHexString(SHA256.HashData(package))}}","Size":{{package.Length}}{{notesJson}}}]}""";
        foreach (string channel in new[] { "win", "linux", "osx" })
        {
            File.WriteAllText(Path.Combine(Feed, $"releases.{channel}.json"), feed);
        }
    }

    private static byte[] Package(string version, string? notes)
    {
        using MemoryStream bytes = new();
        using (ZipArchive zip = new(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using StreamWriter manifest = new(zip.CreateEntry($"{PackId}.nuspec").Open(), Encoding.UTF8);
            manifest.Write(
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                <metadata>
                <id>{PackId}</id>
                <title>ModShelf</title>
                <description>ModShelf</description>
                <authors>ModShelf</authors>
                <version>{version}</version>
                <releaseNotes>{notes}</releaseNotes>
                </metadata>
                </package>
                """);
        }

        return bytes.ToArray();
    }

    private sealed class SynchronousProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
