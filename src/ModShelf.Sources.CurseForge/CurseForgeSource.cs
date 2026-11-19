using System.Globalization;

using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.CurseForge.Accounts;
using ModShelf.Sources.CurseForge.Api;
using ModShelf.Sources.CurseForge.Catalog;
using ModShelf.Sources.CurseForge.Resources;

namespace ModShelf.Sources.CurseForge;

/// <summary>
/// CurseForge (<c>curseforge.com</c>), through its Core API, which answers only with an API key: each user links their
/// own, since CurseForge's terms let no one share one (ADR 0107). Games are named by their slugs, such as
/// <c>minecraft</c>; mods and files by CurseForge's numbers. Every request goes through the client the app sets up for
/// the source.
/// </summary>
public sealed partial class CurseForgeSource : IModSource
{
    /// <summary>
    /// The source's ID. It's stored with every job, so it never changes.
    /// </summary>
    internal static readonly SourceId CurseForgeId = new("curseforge");

    private readonly CurseForgeApiClient _api;
    private readonly CurseForgeGames _games;
    private readonly CurseForgeOptions _options;
    private readonly ILogger<CurseForgeSource> _logger;

    internal CurseForgeSource(CurseForgeApiClient api, CurseForgeGames games, CurseForgeOptions options, ILogger<CurseForgeSource> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        _games = games;
        _options = options;
        _logger = logger;
        Strategies = [new CurseForgeKeyStrategy()];
    }

    public SourceId Id => CurseForgeId;

    public string DisplayName => Strings.DisplayName;

    /// <summary>
    /// None: ModShelf reads no following or approval of CurseForge's.
    /// </summary>
    public SiteWords Words => SiteWords.None;

    private static string Number(long number) => number.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// CurseForge's number for the mod.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't CurseForge's.</exception>
    private static long ModId(ModRef mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        GameKey(mod.Game);
        return long.TryParse(mod.Id, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0
            ? id
            : throw new ArgumentException($"The mod '{mod.Id}' isn't CurseForge's: its mods are numbers.", nameof(mod));
    }

    /// <summary>
    /// CurseForge's number for the file.
    /// </summary>
    /// <exception cref="ArgumentException">The file isn't CurseForge's.</exception>
    private static long FileId(ModFileRef file)
    {
        ArgumentNullException.ThrowIfNull(file);
        ModId(file.Mod);
        return long.TryParse(file.Id, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0
            ? id
            : throw new ArgumentException($"The file '{file.Id}' isn't CurseForge's: its files are numbers.", nameof(file));
    }

    /// <exception cref="ArgumentException">The game isn't CurseForge's.</exception>
    private static string GameKey(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Source == CurseForgeId
            ? game.Key
            : throw new ArgumentException($"The game '{game.Key}' of {game.Source} isn't CurseForge's.", nameof(game));
    }

    /// <summary>
    /// The game, by its slug.
    /// </summary>
    /// <exception cref="ArgumentException">The game isn't CurseForge's.</exception>
    /// <exception cref="HttpRequestException">CurseForge has no such game.</exception>
    private async Task<CurseForgeGame> GameAsync(GameRef game, CancellationToken cancellationToken)
    {
        string key = GameKey(game);
        return await _games.BySlugAsync(key, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException($"CurseForge has no game called {key}.", null, System.Net.HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The mod, as CurseForge describes it, checked to be the game's.
    /// </summary>
    /// <exception cref="HttpRequestException">CurseForge has no such mod of the game.</exception>
    private async Task<(CurseForgeMod Mod, CurseForgeGame Game)> ModAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        long id = ModId(modRef);
        CurseForgeGame game = await GameAsync(modRef.Game, cancellationToken).ConfigureAwait(false);
        CurseForgeMod mod = (await _api.GetAsync<CurseForgeMod>($"v1/mods/{Number(id)}", cancellationToken).ConfigureAwait(false)).Data
            ?? throw new HttpRequestException("CurseForge answered with no mod.", null, System.Net.HttpStatusCode.NotFound);
        return mod.GameId == game.Id
            ? (mod, game)
            : throw new HttpRequestException($"CurseForge's mod {Number(id)} isn't one of {game.Name}'s.", null, System.Net.HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The mod's page on CurseForge's website, as CurseForge gives it; or, for one it gives none, where its page would be.
    /// </summary>
    private Uri PageOf(CurseForgeMod mod, CurseForgeGame game)
        => WebLink(mod.Links?.WebsiteUrl)
            ?? new Uri(_options.WebAddress, $"{game.Slug}/mods/{Uri.EscapeDataString(mod.Slug ?? Number(mod.Id))}");
}
