using ModShelf.Application.Sources;
using ModShelf.Cli.Common;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Cli.Tests.Common;

/// <summary>
/// Which sites could have a game named by its key alone: those whose games' keys look like it.
/// </summary>
public sealed class GameSitesTests
{
    private static readonly SourceId Named = new("named");
    private static readonly SourceId Numbered = new("numbered");
    private static readonly SourceId Searching = new("searching");

    private static readonly Registry Sources = new(new Site(Named, key => key.Any(char.IsAsciiLetter)), new Site(Numbered, key => key.All(char.IsAsciiDigit)));

    [Theory]
    [InlineData("fallout4", "named")]
    [InlineData("16951", "numbered")]
    public void OnlyTheSitesWhoseKeysLookLikeIt(string key, string site)
    {
        Assert.Equal([new SourceId(site)], GameSites.ThatCouldHave(key, [Named, Numbered], Sources));
    }

    [Fact]
    public void ASiteWithoutGamesToSayOfCouldHaveAny()
    {
        Assert.Equal([Named, Searching], GameSites.ThatCouldHave("fallout4", [Named, Numbered, Searching], Sources));
    }

    [Fact]
    public void EverySiteWhenNoneCould()
    {
        Assert.Equal([Named, Numbered], GameSites.ThatCouldHave("--", [Named, Numbered], Sources));
    }

    /// <summary>
    /// A site whose games' keys look as <paramref name="looksLike"/> says.
    /// </summary>
    private sealed class Site(SourceId id, Func<string, bool> looksLike) : IModCatalog
    {
        public SourceId Id => id;

        public string DisplayName => id.Value;

        public SiteWords Words => SiteWords.None;

        public bool CouldBeGameKey(string key) => looksLike(key);

        public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Registry(params IModSource[] sources) : ISourceRegistry
    {
        public IEnumerable<T> All<T>()
            where T : IModSource
            => sources.OfType<T>();

        public T? Find<T>(SourceId id)
            where T : class, IModSource
            => sources.OfType<T>().FirstOrDefault(source => source.Id == id);
    }
}
