using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Links;
using ModShelf.Sources.DegMods.Resources;

namespace ModShelf.Sources.DegMods;

/// <summary>
/// DEG Mods (<c>degmods.com</c>), whose mods are Nostr events on its relays, read over WebSockets, and whose files are on
/// file servers that name them by their SHA-256 (ADR 0090). No account is needed. Every event's signature is checked,
/// and DEG Mods' moderation applied. A game is its name, lowercase; a mod its author and its <c>d</c> tag.
/// </summary>
public sealed partial class DegModsSource
{
    /// <summary>
    /// The source's ID. It's stored with every job, so it never changes.
    /// </summary>
    internal static readonly SourceId DegModsId = new("degmods");

    private readonly ModIndex _index;
    private readonly DegModsOptions _options;
    private readonly IHttpClientFactory _clients;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DegModsSource> _logger;

    internal DegModsSource(ModIndex index, DegModsOptions options, IHttpClientFactory clients, TimeProvider timeProvider, ILogger<DegModsSource> logger)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _index = index;
        _options = options;
        _clients = clients;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public SourceId Id => DegModsId;

    public string DisplayName => Strings.DisplayName;

    /// <summary>
    /// DEG Mods counts nothing ModShelf shows, and ModShelf follows no mod there.
    /// </summary>
    public SiteWords Words => SiteWords.None;

    /// <summary>
    /// The game's key: its name, lowercase.
    /// </summary>
    /// <exception cref="ArgumentException">The game isn't DEG Mods'.</exception>
    private static string GameKey(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Source == DegModsId
            ? game.Key
            : throw new ArgumentException($"The game '{game.Key}' of {game.Source} isn't DEG Mods'.", nameof(game));
    }

    /// <summary>
    /// The mod's author and <c>d</c> tag.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't DEG Mods'.</exception>
    private static (string Author, string D) ModKey(ModRef mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        GameKey(mod.Game);
        return ModEvents.TryReadKey(mod.Id, out string author, out string d)
            ? (author, d)
            : throw new ArgumentException($"The mod '{mod.Id}' isn't DEG Mods': its mods are an author's key and a name, as <author>:<d>.", nameof(mod));
    }

    private static GameRef GameOf(DegMod mod) => new(DegModsId, ModEvents.GameKey(mod.Game));

    private static ModRef ModOf(DegMod mod) => new(GameOf(mod), mod.Key);

    private Uri ModPage(DegMod mod) => DegModsLink.ModPage(_options.SiteAddress, mod.Kind, mod.Author, mod.D);

    /// <summary>
    /// The mod, read afresh, as DEG Mods shows it under the game asked.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't DEG Mods'.</exception>
    /// <exception cref="HttpRequestException">
    /// The relays couldn't be read; with 404 when the mod doesn't exist, DEG Mods hides it, or lists it under another game.
    /// </exception>
    private async Task<DegMod> ShownAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        (string author, string d) = ModKey(modRef);
        (ModStatus status, DegMod? mod) = await _index.FindAsync(author, d, cancellationToken).ConfigureAwait(false);
        return status switch
        {
            ModStatus.Hidden => throw Hidden(),
            ModStatus.Shown when GameOf(mod!) == modRef.Game => mod!,
            ModStatus.Shown => throw new HttpRequestException("DEG Mods lists the mod under another game now.", null, System.Net.HttpStatusCode.NotFound),
            _ => throw new HttpRequestException("DEG Mods has no such mod, or its author deleted it.", null, System.Net.HttpStatusCode.NotFound),
        };
    }

    private HttpRequestException Hidden()
    {
        LogHidden(_logger);
        return new HttpRequestException("DEG Mods' moderation hides this mod, so ModShelf doesn't show or download it.", null, System.Net.HttpStatusCode.NotFound);
    }
}
