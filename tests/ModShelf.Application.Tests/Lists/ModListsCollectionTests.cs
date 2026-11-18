using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Library;
using ModShelf.Application.Lists;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Collections;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Lists;

/// <summary>
/// A site's collection read as a list: a mod for each of its files' mods, in its order, to watch, with the optional
/// files marked, and what it needs from elsewhere named; any link no site reads as one, nothing.
/// </summary>
public sealed class ModListsCollectionTests : IDisposable
{
    private static readonly Uri Link = new("https://collections.example/essentials");
    private static readonly ModRef SkyUi = new(FakeCollectionSite.Game, "12604");
    private static readonly ModRef Skse = new(FakeCollectionSite.Game, "30379");

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly FakeCollectionSite _site = new();
    private readonly RecordingDownloadService _downloads = new();
    private readonly InMemoryLibraryRepository _repository = new();
    private readonly FakeLibraryStorage _storage = new();
    private readonly InMemoryLibraryHistory _history = new();
    private readonly RecordingLogger<ModLists> _logger = new();
    private readonly SettingsService _settings;
    private readonly LibraryDownloads _libraryDownloads;
    private readonly ModLists _lists;

    public ModListsCollectionTests()
    {
        _settings = new SettingsService(new InMemorySettingsStore(), new FakeUserFolders(), _repository, new InMemoryDownloadJobRepository([]));
        SourceRegistry registry = new([new FakeModSite(), _site]);
        LibraryChanges changes = new();
        ModLibrary library = new(registry, _repository, _settings, _storage, _downloads, _downloads, changes, _history.History(_time), _time, new RecordingLogger<ModLibrary>());
        _libraryDownloads = new(registry, _downloads, _repository, _settings, _storage, changes, _history.History(_time), _time, new RecordingLogger<LibraryDownloads>());
        _lists = new ModLists(library, _libraryDownloads, registry, _time, _logger);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _libraryDownloads.Dispose();
        _settings.Dispose();
    }

    [Fact]
    public async Task ReadsACollectionAsAListOfItsModsToWatchWithTheirFiles()
    {
        _site.Collections[Link] = new CollectionRevision(
            "Essentials",
            FakeCollectionSite.Game,
            Link,
            12,
            [
                new CollectionFile(new ModFileRef(SkyUi, "35407"), "SkyUI", "SkyUI 5.2", "5.2", ModFileCategory.Main, optional: false, "schlangster"),
                new CollectionFile(new ModFileRef(Skse, "40002"), "SKSE", "SKSE 2.8", "2.8", ModFileCategory.Main),
                new CollectionFile(new ModFileRef(SkyUi, "35408"), "SkyUI", "SkyUI Extras", null, ModFileCategory.Optional, optional: true, "schlangster"),
            ],
            [new CollectionResource("ENB", new Uri("https://enb.example/")), new CollectionResource("A bundled patch", optional: true)],
            filesUnavailable: 2);

        ModList? list = await _lists.ReadCollectionAsync(Link, Cancellation);

        Assert.NotNull(list);
        Assert.Equal(("Essentials", TestData.Now, 2), (list.Title, list.CreatedAt, list.FilesUnavailable));
        Assert.Equal(
            [
                new ListedMod(SkyUi, "SkyUI", "schlangster", null, Watched: true, Pinned: false, Note: null, []),
                new ListedMod(Skse, "SKSE", null, null, Watched: true, Pinned: false, Note: null, []),
            ],
            list.Mods.Select(mod => mod with { Files = [] }));
        Assert.Equal(
            [
                new ListedFile(new ModFileRef(SkyUi, "35407"), "SkyUI 5.2", "5.2", ModFileCategory.Main, Optional: false),
                new ListedFile(new ModFileRef(SkyUi, "35408"), "SkyUI Extras", null, ModFileCategory.Optional, Optional: true),
            ],
            list.Mods[0].Files);
        Assert.Equal(
            [new ListedElsewhere("ENB", new Uri("https://enb.example/"), Optional: false), new ListedElsewhere("A bundled patch", null, Optional: true)],
            list.Elsewhere);
        Assert.Contains(_logger.Entries, entry => entry.Contains("Read collection Essentials of fake-collections/skyrimspecialedition", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALinkNoSiteReadsAsACollectionIsNone()
    {
        Assert.Null(await _lists.ReadCollectionAsync(new Uri("https://elsewhere.example/list"), Cancellation));
        Assert.Equal([new Uri("https://elsewhere.example/list")], _site.Asked);
    }

    [Fact]
    public async Task WhatTheSiteRefusesIsTheCallersToSay()
    {
        _site.Collections[Link] = new CollectionRevision("Essentials", FakeCollectionSite.Game, Link, null, [], []);
        _site.Refusal = new HttpRequestException("No such collection.");

        await Assert.ThrowsAsync<HttpRequestException>(() => _lists.ReadCollectionAsync(Link, Cancellation));
    }

    [Fact]
    public async Task ACollectionsListPlansAndImportsAsAListFileDoes()
    {
        _site.Collections[Link] = new CollectionRevision(
            "Essentials",
            FakeCollectionSite.Game,
            Link,
            null,
            [new CollectionFile(new ModFileRef(SkyUi, "35407"), "SkyUI", "SkyUI 5.2")],
            []);
        ModList list = (await _lists.ReadCollectionAsync(Link, Cancellation))!;

        ListImportPlan plan = await _lists.PlanAsync(list, Cancellation);

        // The fake collection site has no catalog, so ModShelf can't reach its mods here, as with a list file's.
        Assert.Equal((0, 1), (plan.NewMods, plan.ModsWithoutSite));
        Assert.Same(list, plan.List);
    }
}
