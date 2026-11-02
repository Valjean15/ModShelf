using System.Net;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Catalog;
using ModShelf.Application.Catalog.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Catalog;

public sealed class ModDetailsTests : IDisposable
{
    private readonly FakeCatalogSource _site = new(TestData.Nexus);
    private readonly InMemoryModDetailsRepository _repository = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly RecordingLogger<ModDetails> _logger = new();
    private readonly SettingsService _settings;
    private readonly ModDetails _details;

    public ModDetailsTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), new InMemoryLibraryRepository(), new InMemoryDownloadJobRepository([]));
        _details = Details(_site);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private ModRef Mod => _site.LinkedFile.Mod;

    public void Dispose()
    {
        _details.Dispose();
        _settings.Dispose();
    }

    [Fact]
    public async Task AsksTheSiteAboutAModNeverSavedAndSavesWhatItSays()
    {
        ModInfo? details = await _details.GetAsync(Mod, Cancellation);

        Assert.Equal(_site.Mod, details);
        Assert.Equal(new SavedModDetails(_site.Mod, TestData.Now), _repository[Mod]);
        Assert.Equal(["GetModAsync"], _site.Asked);
    }

    [Fact]
    public async Task ShowsSavedDetailsUntilTheyreADayOld()
    {
        ModInfo saved = new(Mod, "SkyUI, as saved");
        _repository.Add(new SavedModDetails(saved, TestData.Now - TimeSpan.FromHours(23)));

        ModInfo? young = await _details.GetAsync(Mod, Cancellation);
        _time.Advance(TimeSpan.FromHours(1));
        ModInfo? old = await _details.GetAsync(Mod, Cancellation);

        Assert.Equal(saved, young);
        Assert.Equal(_site.Mod, old);
        Assert.Equal(["GetModAsync"], _site.Asked);
    }

    [Fact]
    public async Task KeepsSavedDetailsAsLongAsTheSettingsSay()
    {
        _store.Settings = new AppSettings { DetailsMaxAge = TimeSpan.FromHours(6) };
        ModInfo saved = new(Mod, "SkyUI, as saved");
        _repository.Add(new SavedModDetails(saved, TestData.Now - TimeSpan.FromHours(6) + TimeSpan.FromSeconds(1)));

        ModInfo? young = await _details.GetAsync(Mod, Cancellation);
        _time.Advance(TimeSpan.FromSeconds(1));
        ModInfo? old = await _details.GetAsync(Mod, Cancellation);

        Assert.Equal(saved, young);
        Assert.Equal(_site.Mod, old);
    }

    [Fact]
    public async Task KeepsSavedDetailsADayWhenTheSettingsCantBeRead()
    {
        _store.Failure = new InvalidDataException("The settings file isn't JSON.");
        ModInfo saved = new(Mod, "SkyUI, as saved");
        _repository.Add(new SavedModDetails(saved, TestData.Now - TimeSpan.FromHours(23)));

        Assert.Equal(saved, await _details.GetAsync(Mod, Cancellation));
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task DetailsSavedInTheFutureAreOld()
    {
        _repository.Add(new SavedModDetails(new ModInfo(Mod, "SkyUI, as saved"), TestData.Now + TimeSpan.FromMinutes(5)));

        Assert.Equal(_site.Mod, await _details.GetAsync(Mod, Cancellation));
    }

    public static TheoryData<Exception> SiteProblems =>
    [
        new HttpRequestException("No route to host."),
        new HttpRequestException("Not found.", null, HttpStatusCode.NotFound),
        new AuthenticationFailedException("The API key was revoked."),
        new TaskCanceledException("The request timed out."),
        new ArgumentException("The site doesn't know this game."),
    ];

    [Theory]
    [MemberData(nameof(SiteProblems))]
    public async Task ShowsTheSavedDetailsHoweverOldWhenTheSiteCantSay(Exception problem)
    {
        SavedModDetails saved = new(new ModInfo(Mod, "SkyUI, as saved"), TestData.Now - TimeSpan.FromDays(30));
        _repository.Add(saved);
        _site.Failures[nameof(IModCatalog.GetModAsync)] = problem;

        ModInfo? details = await _details.GetAsync(Mod, Cancellation);

        Assert.Equal(saved.Details, details);
        Assert.Equal(saved, _repository[Mod]);
        Assert.Contains(problem.Message, Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HasNoDetailsWhenNoneWereSavedAndTheSiteCantSay()
    {
        _site.Failures[nameof(IModCatalog.GetModAsync)] = new HttpRequestException("No route to host.");

        Assert.Null(await _details.GetAsync(Mod, Cancellation));
        Assert.Equal(0, _repository.Saves);
    }

    [Fact]
    public async Task AsksTheSiteAgainAfterItCouldntSay()
    {
        _site.Failures[nameof(IModCatalog.GetModAsync)] = new HttpRequestException("No route to host.");

        await _details.GetAsync(Mod, Cancellation);
        ModInfo? details = await _details.GetAsync(Mod, Cancellation);

        Assert.Equal(_site.Mod, details);
        Assert.Equal(["GetModAsync", "GetModAsync"], _site.Asked);
    }

    [Fact]
    public async Task ShowsTheSavedDetailsOfAModWhoseSourceCantDescribeIt()
    {
        SavedModDetails saved = new(new ModInfo(ModOn(new SourceId("gone"), "1"), "Gone"), TestData.Now - TimeSpan.FromDays(30));
        _repository.Add(saved);
        using ModDetails details = Details(new NotACatalog(new SourceId("gone")));

        Assert.Equal(saved.Details, await details.GetAsync(saved.Details.Mod, Cancellation));
        Assert.Null(await details.GetAsync(ModOn(new SourceId("unknown"), "1"), Cancellation));
    }

    [Fact]
    public async Task ReadsDetailsThatCantBeReadFromTheSiteAgain()
    {
        _repository.FindFailure = new InvalidDataException("A link isn't one.");

        ModInfo? details = await _details.GetAsync(Mod, Cancellation);
        ModInfo? saved = await _details.GetSavedAsync(Mod, Cancellation);

        Assert.Equal(_site.Mod, details);
        Assert.Null(saved);
        Assert.Equal(2, _logger.Entries.Count);
        Assert.All(_logger.Entries, entry => Assert.StartsWith("Warning: ", entry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivesTheSavedDetailsHoweverOldWithoutAskingTheSite()
    {
        SavedModDetails saved = new(new ModInfo(Mod, "SkyUI, as saved"), TestData.Now - TimeSpan.FromDays(30));
        _repository.Add(saved);

        ModInfo? details = await _details.GetSavedAsync(Mod, Cancellation);
        ModInfo? none = await _details.GetSavedAsync(new ModRef(Mod.Game, "1"), Cancellation);

        Assert.Equal(saved.Details, details);
        Assert.Null(none);
        Assert.Empty(_site.Asked);
    }

    [Fact]
    public async Task CallersAskingMeanwhileShareOneRead()
    {
        HeldCatalog site = new();
        using ModDetails details = Details(site);
        ModRef mod = ModOn(site.Id, "1");
        using CancellationTokenSource leaving = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);

        Task<ModInfo?> left = details.GetAsync(mod, leaving.Token);
        Task<ModInfo?> first = details.GetAsync(mod, Cancellation);
        Task<ModInfo?> second = details.GetAsync(mod, Cancellation);
        await leaving.CancelAsync();
        site.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => left);
        Assert.Equal(mod, (await first)?.Mod);
        Assert.Equal(mod, (await second)?.Mod);
        Assert.Equal(1, site.Started);
    }

    [Fact]
    public async Task AsksTheSitesAboutAFewModsAtOnce()
    {
        HeldCatalog site = new();
        using ModDetails details = Details(site);

        Task<ModInfo?>[] asked = [.. Enumerable.Range(1, ModDetails.SiteReadsAtOnce + 2)
            .Select(id => details.GetAsync(ModOn(site.Id, $"{id}"), Cancellation))];
        int startedAtFirst = site.Started;
        site.Release();
        await Task.WhenAll(asked);

        Assert.Equal(ModDetails.SiteReadsAtOnce, startedAtFirst);
        Assert.Equal(ModDetails.SiteReadsAtOnce + 2, site.Started);
        Assert.All(asked, task => Assert.NotNull(task.Result));
    }

    private static ModRef ModOn(SourceId source, string id) => new(new GameRef(source, "skyrimspecialedition"), id);

    private ModDetails Details(IModSource source) => new(new SourceRegistry([source]), _repository, _settings, _time, _logger);

    /// <summary>
    /// A source with no catalog.
    /// </summary>
    private sealed class NotACatalog(SourceId id) : IModSource
    {
        public SourceId Id { get; } = id;

        public string DisplayName => Id.Value;
    }

    /// <summary>
    /// A catalog that holds every mod read until released, as a slow site does, and counts the reads it started.
    /// </summary>
    private sealed class HeldCatalog : IModCatalog
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public SourceId Id { get; } = new("held");

        public string DisplayName => "Held";

        public int Started => Volatile.Read(ref _started);

        public void Release() => _released.SetResult();

        public async Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _started);
            await _released.Task.WaitAsync(cancellationToken);
            return new ModInfo(modRef, $"Mod {modRef.Id}");
        }

        public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
