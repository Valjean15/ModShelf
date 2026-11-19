using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.Thunderstore.Catalog;
using ModShelf.Sources.Thunderstore.Tests.Common;

namespace ModShelf.Sources.Thunderstore.Tests.Catalog;

/// <summary>
/// Each community's listing: read through its index and chunks, kept for an hour, then asked again with
/// <c>If-Modified-Since</c>; shared by callers at once; kept when it can't be read again (ADR 0103).
/// </summary>
public sealed class ThunderstoreListingsTests : IDisposable
{
    private const string Community = "game";

    private readonly ThunderstoreServer _server = new();
    private ServiceProvider? _services;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private ThunderstoreListings Listings => (_services ??= _server.BuildServices()).GetRequiredService<ThunderstoreListings>();

    public void Dispose()
    {
        _services?.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task ReadsEveryChunkInOrder()
    {
        _server.ServeListing(Community, [TestPackage.Chunk(Package("A"), Package("B")), TestPackage.Chunk(Package("C"))]);

        Listing listing = await Listings.GetAsync(Community, Cancellation);

        Assert.Equal(["Team-A", "Team-B", "Team-C"], listing.Packages.Select(package => package.Name.FullName));
        Assert.Equal(TestPackage.January, listing.LastModified);
        Assert.Equal(_server.Clock.GetUtcNow(), listing.ReadAt);
        Assert.Contains(_server.Logs.Entries, entry => entry.Contains("Read Thunderstore's listing of game: 3 packages in 2 chunks", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadsBlobsWhetherOrNotTheyreCompressed()
    {
        _server.ServeListing(Community, [TestPackage.Chunk(Package("A"))], gzip: false);

        Listing listing = await Listings.GetAsync(Community, Cancellation);

        Assert.Equal("Team-A", Assert.Single(listing.Packages).Name.FullName);
    }

    [Fact]
    public async Task KeepsEachPackageSmall()
    {
        _server.ServeListing(Community, [TestPackage.Chunk(Package("A") with
        {
            Categories = ["Mods", "Tools"],
            Likes = 7,
            Adult = true,
            Description = "  Shows the damage.  ",
            Dependencies = ["Team-B-1.0.0"],
            Versions = [new("1.1.0", TestPackage.January.AddDays(2), 2048, 5), new("1.0.0", TestPackage.January, 1024, 20)],
        })]);

        ListedPackage package = Assert.Single((await Listings.GetAsync(Community, Cancellation)).Packages);

        Assert.Equal(["Mods", "Tools"], package.Categories);
        Assert.Equal((7L, true, "Shows the damage.", 25L), (package.Likes, package.HasAdultContent, package.Description, package.Downloads));
        Assert.Equal("https://ccdn.thunderstore.io/live/repository/icons/Team-A-1.1.0.png", package.Icon?.AbsoluteUri);
        Assert.Equal(["Team-B-1.0.0"], package.Dependencies);
        Assert.Equal(
            [new ListedVersion("1.1.0", TestPackage.January.AddDays(2), 2048, 5), new ListedVersion("1.0.0", TestPackage.January, 1024, 20)],
            package.Versions);
    }

    [Fact]
    public async Task LeavesOutPackagesWithNoVersionOrANameThunderstoreDoesntGive()
    {
        JsonArray chunk = TestPackage.Chunk(Package("A"), Package("B"), new TestPackage("Bad Team", "C"));
        chunk[1]!["versions"] = new JsonArray();
        _server.ServeListing(Community, [chunk]);

        Listing listing = await Listings.GetAsync(Community, Cancellation);

        Assert.Equal("Team-A", Assert.Single(listing.Packages).Name.FullName);
        Assert.Contains(_server.Logs.Entries, entry => entry.Contains("Left out 2 packages", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindsAPackageByItsNameInAnyCase()
    {
        _server.ServeListing(Community, [TestPackage.Chunk(Package("DamageMeter"))]);

        Listing listing = await Listings.GetAsync(Community, Cancellation);

        Assert.Equal("Team-DamageMeter", listing.Find("team-damagemeter")?.Name.FullName);
        Assert.Null(listing.Find("Team-Other"));
    }

    [Fact]
    public async Task UsesTheListingForAnHourThenAsksWhetherItChanged()
    {
        _server.ServeListing(Community, [TestPackage.Chunk(Package("A"))]);
        Listing first = await Listings.GetAsync(Community, Cancellation);
        int asked = _server.Requests.Count;

        _server.Clock.Advance(TimeSpan.FromMinutes(59));
        Listing again = await Listings.GetAsync(Community, Cancellation);
        Assert.Same(first, again);
        Assert.Equal(asked, _server.Requests.Count);

        _server.Clock.Advance(TimeSpan.FromMinutes(2));
        Listing checkedAgain = await Listings.GetAsync(Community, Cancellation);

        // One request, answered 304: the chunks aren't asked again.
        Assert.Equal(asked + 1, _server.Requests.Count);
        Assert.Equal(TestPackage.January.ToString("R"), _server.Requests[^1].RequestMessage!.Headers!["If-Modified-Since"].Single());
        Assert.Same(first.Packages, checkedAgain.Packages);
        Assert.Equal(_server.Clock.GetUtcNow(), checkedAgain.ReadAt);
        Assert.Contains(_server.Logs.Entries, entry => entry.Contains("is unchanged since", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnIndexPointingToTheBlobItWasReadFromIsUnchanged()
    {
        // As Thunderstore answers: a redirect whatever it's asked, to a blob whose link names its SHA-256.
        _server.ServeListing(Community, [TestPackage.Chunk(Package("A"))], changed: true);
        Listing first = await Listings.GetAsync(Community, Cancellation);
        int asked = _server.Requests.Count;

        _server.Clock.Advance(TimeSpan.FromHours(1));
        Listing again = await Listings.GetAsync(Community, Cancellation);

        Assert.Equal(asked + 1, _server.Requests.Count);
        Assert.Same(first.Packages, again.Packages);
        Assert.Equal((_server.Clock.GetUtcNow(), first.Blob), (again.ReadAt, again.Blob));
        Assert.EndsWith("/live/blob-storage/game-index.blob", first.Blob!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains(_server.Logs.Entries, entry => entry.Contains("is unchanged since", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadsAChangedListingAfterAnHour()
    {
        _server.ServeListing(Community, [TestPackage.Chunk(Package("A"))]);
        await Listings.GetAsync(Community, Cancellation);
        _server.Reset();
        _server.ServeListing(Community, [TestPackage.Chunk(Package("A"), Package("B"))], lastModified: TestPackage.January.AddHours(1), changed: true, stamp: "-later");

        _server.Clock.Advance(TimeSpan.FromHours(1));
        Listing listing = await Listings.GetAsync(Community, Cancellation);

        Assert.Equal(["Team-A", "Team-B"], listing.Packages.Select(package => package.Name.FullName));
        Assert.Equal(TestPackage.January.AddHours(1), listing.LastModified);
    }

    [Fact]
    public async Task CallersAtOnceShareOneRead()
    {
        _server.ServeListing(Community, [TestPackage.Chunk(Package("A"))]);

        Listing[] listings = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Listings.GetAsync(Community, Cancellation)));

        Assert.All(listings, listing => Assert.Same(listings[0], listing));
        Assert.Single(_server.Asked, path => path == $"/c/{Community}/api/v1/package-listing-index/");
    }

    [Fact]
    public async Task KeepsTheListingItCouldntReadAgainAndTriesAgainLater()
    {
        _server.ServeListing(Community, [TestPackage.Chunk(Package("A"))]);
        Listing first = await Listings.GetAsync(Community, Cancellation);
        _server.Reset();
        _server.Answer($"/c/{Community}/api/v1/package-listing-index/", HttpStatusCode.InternalServerError);

        _server.Clock.Advance(TimeSpan.FromHours(1));
        Listing kept = await Listings.GetAsync(Community, Cancellation);
        int asked = _server.Requests.Count;
        _server.Clock.Advance(ThunderstoreListings.RetryAfterFailure - TimeSpan.FromSeconds(1));
        await Listings.GetAsync(Community, Cancellation);
        Assert.Equal(asked, _server.Requests.Count);
        _server.Clock.Advance(TimeSpan.FromSeconds(2));
        await Listings.GetAsync(Community, Cancellation);

        Assert.Same(first.Packages, kept.Packages);
        Assert.Equal(asked + 1, _server.Requests.Count);
        Assert.Contains(_server.Logs.Entries, entry => entry.Contains("couldn't be read again", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FailsWhenItHasNoListingToKeep(HttpStatusCode status)
    {
        _server.Answer($"/c/{Community}/api/v1/package-listing-index/", status);

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => Listings.GetAsync(Community, Cancellation));

        Assert.Equal(status, failed.StatusCode);
    }

    [Fact]
    public async Task FailsAllWhenOneChunkCantBeRead()
    {
        IReadOnlyList<string> chunks = _server.ServeListing(Community, [TestPackage.Chunk(Package("A")), TestPackage.Chunk(Package("B"))]);
        _server.Answer(chunks[1], HttpStatusCode.NotFound);

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => Listings.GetAsync(Community, Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, failed.StatusCode);
    }

    [Theory]
    [InlineData("https://elsewhere.example/chunk.blob")]
    [InlineData("http://thunderstore.io.example/chunk.blob")]
    public async Task FollowsNoLinkToAnotherHost(string link)
    {
        _server.ServeListing(Community, [TestPackage.Chunk(Package("A"))]);
        _server.Blob($"/live/blob-storage/{Community}-index.blob", new JsonArray(link).ToJsonString(), first: true);

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => Listings.GetAsync(Community, Cancellation));

        Assert.Contains("isn't Thunderstore's", failed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Asked, path => path.EndsWith("chunk.blob", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusesAChunkThatIsntAListOfPackages()
    {
        IReadOnlyList<string> chunks = _server.ServeListing(Community, [TestPackage.Chunk(Package("A"))]);
        _server.Blob(chunks[0], "<html>Not here</html>", first: true);

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => Listings.GetAsync(Community, Cancellation));

        Assert.Equal(HttpRequestError.InvalidResponse, failed.HttpRequestError);
    }

    [Fact]
    public async Task RefusesABlobThatUnpacksToTooMuch()
    {
        IReadOnlyList<string> chunks = _server.ServeListing(Community, [TestPackage.Chunk(Package("A"))]);
        StringBuilder huge = new("[");
        huge.Append(' ', (int)(Api.ThunderstoreApiClient.MostUnpackedBytes + 1024));
        huge.Append(']');
        _server.Blob(chunks[0], huge.ToString(), first: true);

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => Listings.GetAsync(Community, Cancellation));

        Assert.IsType<InvalidDataException>(failed.InnerException);
    }

    [Fact]
    public async Task ReadsALargeListingAsItStreams()
    {
        // As many packages as a big community's chunk holds, each with versions, read without holding the chunk's JSON.
        TestPackage[] packages = [.. Enumerable.Range(0, 6000).Select(index => Package($"P{index}") with
        {
            Versions = [.. Enumerable.Range(0, 5).Select(version => new TestVersion($"1.0.{4 - version}", TestPackage.January.AddDays(-version)))],
            Dependencies = ["BepInEx-BepInExPack-5.4.2305"],
        })];
        _server.ServeListing(Community, [TestPackage.Chunk(packages[..3000]), TestPackage.Chunk(packages[3000..])]);

        Listing listing = await Listings.GetAsync(Community, Cancellation);

        Assert.Equal(6000, listing.Packages.Count);
        Assert.All(listing.Packages, package => Assert.Equal(5, package.Versions.Count));
        Assert.Equal("Team-P5999", listing.Packages[^1].Name.FullName);
    }

    private static TestPackage Package(string name) => new("Team", name);
}
