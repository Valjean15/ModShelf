using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.ModIo.Accounts;
using ModShelf.Sources.ModIo.Api;
using ModShelf.Sources.ModIo.Catalog;
using ModShelf.Sources.ModIo.Links;
using ModShelf.Sources.ModIo.Resources;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// mod.io (<c>mod.io</c>), through its REST API, which answers only with an API key or an access token the user made
/// for themselves on mod.io (ADR 0089). Games, mods and files are named by mod.io's IDs; a game's own requests go to the
/// game's API host, the others to <c>api.mod.io</c>. Every request goes through the client the app sets up for the
/// source, at mod.io's pace.
/// </summary>
public sealed partial class ModIoSource
{
    /// <summary>
    /// The source's ID. It's stored with every job, so it never changes.
    /// </summary>
    internal static readonly SourceId ModIoId = new("modio");

    private readonly ModIoApiClient _api;
    private readonly ModIoOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ModIoSource> _logger;

    // What each game read lets apps do (api_access_options), so a download asks for an access token only where needed.
    private readonly ConcurrentDictionary<long, int> _access = new();

    // Each game's ID by the name in its links, once looked up: a game's name in links doesn't change.
    private readonly ConcurrentDictionary<string, long> _gamesByName = new(StringComparer.Ordinal);

    internal ModIoSource(ModIoApiClient api, ModIoOptions options, TimeProvider timeProvider, ILogger<ModIoSource> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        Strategies = [new ModIoAccessStrategy()];
    }

    public SourceId Id => ModIoId;

    public string DisplayName => Strings.DisplayName;

    /// <summary>
    /// mod.io counts positive ratings, and its users subscribe to mods, which ModShelf reads but never changes: games
    /// install what's subscribed.
    /// </summary>
    public SiteWords Words { get; } = new()
    {
        Followed = Strings.WordFollowed,
        Approval = Strings.WordApproval,
        Approvals = Strings.WordApprovals,
    };

    /// <summary>
    /// The game's ID on mod.io.
    /// </summary>
    /// <exception cref="ArgumentException">The game isn't mod.io's.</exception>
    private static long GameId(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Source == ModIoId && ModIoLink.IsId(game.Key)
            ? ModIoLink.Id(game.Key)
            : throw new ArgumentException($"The game '{game.Key}' of {game.Source} isn't mod.io's.", nameof(game));
    }

    /// <summary>
    /// The mod's game's ID and its own on mod.io.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't mod.io's.</exception>
    private static (long Game, long Mod) ModId(ModRef mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        long game = GameId(mod.Game);
        return ModIoLink.IsId(mod.Id)
            ? (game, ModIoLink.Id(mod.Id))
            : throw new ArgumentException($"The mod '{mod.Id}' isn't mod.io's: its mods are numbers.", nameof(mod));
    }

    private static GameRef GameOf(long id) => new(ModIoId, Number(id));

    private static string Number(long number) => number.ToString(CultureInfo.InvariantCulture);

    private static string Escaped(string text) => Uri.EscapeDataString(text);

    /// <summary>
    /// An address on the API for what isn't one game's, such as <c>games</c> or <c>me</c>.
    /// </summary>
    private Uri Api(string relative) => new(_options.ApiAddress, relative);

    /// <summary>
    /// The game's API, as mod.io asks apps to call it.
    /// </summary>
    private Uri GameApi(long game) => new(string.Format(CultureInfo.InvariantCulture, _options.GameApiAddress, game), UriKind.Absolute);

    /// <summary>
    /// An address on the game's API, such as <c>games/6715/mods</c>.
    /// </summary>
    private Uri GameApi(long game, string relative) => new(GameApi(game), relative);

    /// <summary>
    /// The mod, with its live file for Windows, which mod.io gives only when asked for a platform's, for a game with files
    /// per platform.
    /// </summary>
    private Task<ModIoMod> ModAsync(long game, long mod, CancellationToken cancellationToken)
        => _api.GetAsync<ModIoMod>(GameApi(game, $"games/{Number(game)}/mods/{Number(mod)}"), ModIoMods.Platform, tokenOnly: null, cancellationToken);
}
