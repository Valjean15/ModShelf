using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.Thunderstore.Catalog;
using ModShelf.Sources.Thunderstore.Tests.Common;

namespace ModShelf.Sources.Thunderstore.Tests;

/// <summary>
/// The source against what Thunderstore answered when recorded: its listing as the CDN serves it, unchanged when asked
/// again, a package listed in several communities, and what doesn't exist.
/// </summary>
public sealed class ThunderstoreSourceRecordedTests : IDisposable
{
    private readonly ThunderstoreServer _server = new();
    private readonly ServiceProvider _services;

    public ThunderstoreSourceRecordedTests()
    {
        _server.ServeRecordings();
        _services = _server.BuildServices();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static GameRef Game { get; } = new(new SourceId("thunderstore"), ThunderstoreSourceTests.Community);

    private static ModRef Mod { get; } = new(Game, $"{ThunderstoreSourceTests.Package.Team}-{ThunderstoreSourceTests.Package.Name}");

    private ThunderstoreSource Source => ThunderstoreServer.SourceOf(_services);

    public void Dispose()
    {
        _services.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task ReadsTheRecordedListingWhole()
    {
        int recorded = ThunderstoreServer.Body("listing-chunk-1").AsArray().Count;

        Listing listing = await _services.GetRequiredService<ThunderstoreListings>().GetAsync(Game.Key, Cancellation);

        Assert.Equal(recorded, listing.Packages.Count);
        Assert.Contains(listing.Packages, package => package.IsDeprecated);
        Assert.NotNull(listing.LastModified);
    }

    [Fact]
    public async Task AsksAgainAfterAnHourAndKeepsTheUnchangedListing()
    {
        Assert.SkipUnless(File.Exists(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Recorded", "listing-index-unchanged.json")), "Thunderstore gave no Last-Modified when recorded.");
        ThunderstoreListings listings = _services.GetRequiredService<ThunderstoreListings>();
        Listing first = await listings.GetAsync(Game.Key, Cancellation);
        int asked = _server.Requests.Count;

        _server.Clock.Advance(TimeSpan.FromHours(1));
        Listing again = await listings.GetAsync(Game.Key, Cancellation);

        Assert.Same(first.Packages, again.Packages);
        Assert.Equal(asked + 1, _server.Requests.Count);
    }

    [Fact]
    public async Task DescribesTheRecordedPackageWithItsVersionsAndNeeds()
    {
        ModInfo mod = await Source.GetModAsync(Mod, Cancellation);
        IReadOnlyList<ModFile> files = await Source.GetFilesAsync(Mod, Cancellation);
        IReadOnlyList<ModRequirement> requirements = await Source.GetRequirementsAsync(Mod, Cancellation);

        Assert.Equal((ThunderstoreSourceTests.Package.Team, ThunderstoreSourceTests.Versions[0]), (mod.Author, mod.Version));
        Assert.Equal(ThunderstoreSourceTests.Versions, files.Select(file => file.Reference.Id));
        Assert.Single(files, file => file.Category == ModFileCategory.Main);
        Assert.NotEmpty(requirements);
        Assert.All(requirements.Where(requirement => requirement.Mod is not null), requirement => Assert.Equal(Game, requirement.Mod!.Game));
    }

    [Fact]
    public async Task SearchesTheRecordedCommunityLeavingOutDeprecatedPackages()
    {
        SearchPage page = await Source.SearchAsync(Game, new SearchQuery(count: SearchQuery.MaxCount), Cancellation);
        int live = ThunderstoreServer.Body("listing-chunk-1").AsArray()
            .Count(package => !package!["is_deprecated"]!.GetValue<bool>() && !package["has_nsfw_content"]!.GetValue<bool>());

        Assert.Equal(live, page.TotalCount);
    }

    [Fact]
    public async Task RefusesALinkToAPackageListedInSeveralCommunities()
    {
        string team = ThunderstoreServer.Body("package-several")["namespace"]!.GetValue<string>();
        string name = ThunderstoreServer.Body("package-several")["name"]!.GetValue<string>();

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => Source.RecognizeAsync(new Uri($"ror2mm://v1/install/thunderstore.io/{team}/{name}/1.0.0/"), Cancellation));

        Assert.Contains($"{team}-{name} is listed for", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhatDoesntExist()
    {
        string package = ThunderstoreServer.Recording("package-not-found")["request"]!["path"]!.GetValue<string>();
        string[] parts = package.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string community = ThunderstoreServer.Recording("community-not-found")["request"]!["path"]!.GetValue<string>().Split('/')[2];

        HttpRequestException noPackage = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source.RecognizeAsync(new Uri($"https://thunderstore.io/package/download/{parts[3]}/{parts[4]}/1.0.0/"), Cancellation));
        HttpRequestException noCommunity = await Assert.ThrowsAsync<HttpRequestException>(
            () => Source.SearchAsync(new GameRef(new SourceId("thunderstore"), community), new SearchQuery(), Cancellation));

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (noPackage.StatusCode, noCommunity.StatusCode));
    }

    [Fact]
    public async Task ReadsTheRecordedChangelog()
    {
        string? markdown = ThunderstoreServer.Body("changelog")["markdown"]?.GetValue<string>();

        IReadOnlyList<ModChangelog> changelogs = await Source.GetChangelogsAsync(Mod, Cancellation);

        Assert.Equal(ThunderstoreChangelogs.Parse(markdown).Select(changelog => changelog.Version), changelogs.Select(changelog => changelog.Version));
    }
}
