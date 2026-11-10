using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A site that finds its games by name, as GameBanana does, by a word of 3 letters or more: its search finds every game
/// it has, as a site's own search can find more than match, and the app matches them itself. It has no mods to show.
/// </summary>
internal sealed class FakeGameSearchSite : IModCatalog, IGameSearch
{
    public static SourceId SourceId { get; } = new("fake-names");

    public SourceId Id => SourceId;

    public string DisplayName => "Fake name search";

    public SiteWords Words => SiteWords.None;

    public List<GameInfo> Games { get; } =
    [
        new(new GameRef(SourceId, "16951"), "Persona 5 Royal (PC)"),
        new(new GameRef(SourceId, "7545"), "Persona 5"),
        new(new GameRef(SourceId, "8502"), "Persona 3 FES"),
        new(new GameRef(SourceId, "5866"), "The Legend of Zelda"),
    ];

    /// <summary>
    /// The words searched for, in order.
    /// </summary>
    public List<string> Asked { get; } = [];

    public Task<IReadOnlyList<GameInfo>> FindGamesAsync(string words, CancellationToken cancellationToken)
    {
        Asked.Add(words);
        return words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => word.Length >= 3)
            ? Task.FromResult<IReadOnlyList<GameInfo>>([.. Games])
            : throw new ArgumentException("Fake name search finds games by a word of 3 letters or more.", nameof(words));
    }

    public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("The app finds this site's games by name.");

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
        => Task.FromResult(Games.FirstOrDefault(listed => listed.Game == game)
            ?? throw new HttpRequestException("No such game.", null, System.Net.HttpStatusCode.NotFound));

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();
}
