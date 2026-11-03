using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Nexus.Api;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// Games' pictures: the <c>GameArtwork</c> query against the v2 GraphQL API gives where each kind of picture is, by the
/// game's number. It's asked once a run, the first time a game is read; when it can't be answered, which is logged,
/// games come without pictures until the app starts again.
/// </summary>
public sealed partial class NexusSource
{
    private const string GameIdField = "{gameId}";

    private static readonly string GameArtworkQuery = ReadQuery("Catalog", "GameArtwork.graphql");

    private Lazy<Task<NexusGameArtwork.SchemaV2?>>? _artwork;

    /// <summary>
    /// Where the games' pictures are, asked once, whoever asks first; null when it couldn't be answered. It isn't the
    /// first caller's to cancel: the answer is every game's.
    /// </summary>
    private Task<NexusGameArtwork.SchemaV2?> ArtworkAsync()
        => LazyInitializer.EnsureInitialized(ref _artwork, () => new Lazy<Task<NexusGameArtwork.SchemaV2?>>(ReadArtworkAsync)).Value;

    private async Task<NexusGameArtwork.SchemaV2?> ReadArtworkAsync()
    {
        try
        {
            NexusGameArtwork found = await _api.QueryAsync<NexusGameArtwork>(GameArtworkQuery, new JsonObject(), CancellationToken.None).ConfigureAwait(false);
            return found.GameArtwork?.SchemaV2;
        }
        catch (Exception exception) when (exception is HttpRequestException or AuthenticationFailedException or JsonException)
        {
            LogArtworkUnread(_logger, exception.Message);
            return null;
        }
    }

    /// <summary>
    /// The picture at <paramref name="template"/> for the game with <paramref name="gameId"/>; null without either, or
    /// when that isn't a web link.
    /// </summary>
    private static Uri? PictureOf(string? template, long? gameId)
        => template is not null && gameId is > 0 && template.Contains(GameIdField, StringComparison.Ordinal)
            ? WebLink(template.Replace(GameIdField, gameId.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            : null;
}
