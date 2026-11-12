using System.Collections.Immutable;
using System.Text.Json.Nodes;

using ModShelf.Domain.Mods;

namespace ModShelf.Infrastructure.Settings;

/// <summary>
/// The games picked for the games installed on this computer: <c>installedGames</c>, by the installed game's store and
/// ID, then by site, the game's key there, or null for none there.
/// </summary>
internal sealed partial class JsonSettingsStore
{
    private const string InstalledGames = "installedGames";

    private static ImmutableDictionary<string, IReadOnlyDictionary<SourceId, string?>> ReadInstalledGames(JsonNode? section)
    {
        ImmutableDictionary<string, IReadOnlyDictionary<SourceId, string?>> games = ImmutableDictionary<string, IReadOnlyDictionary<SourceId, string?>>.Empty;
        if (section is null)
        {
            return games;
        }

        if (section is not JsonObject installed)
        {
            throw new InvalidDataException($"{InstalledGames} must hold the installed games by their store and ID.");
        }

        foreach ((string game, JsonNode? node) in installed)
        {
            if (!IsInstalledGame(game) || node is not JsonObject sites)
            {
                throw new InvalidDataException($"{InstalledGames}.{game} must name a store and a game, such as steam/489830, and hold its games by site.");
            }

            ImmutableDictionary<SourceId, string?> picked = ImmutableDictionary<SourceId, string?>.Empty;
            foreach ((string site, JsonNode? key) in sites)
            {
                picked = picked.SetItem(new SourceId(site), key switch
                {
                    null => null,
                    JsonValue value when value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) => text,
                    _ => throw new InvalidDataException($"{InstalledGames}.{game}.{site} must be a game's key, or null for none."),
                });
            }

            if (!picked.IsEmpty)
            {
                games = games.SetItem(game, picked);
            }
        }

        return games;
    }

    private static void WriteInstalledGames(JsonObject file, IReadOnlyDictionary<string, IReadOnlyDictionary<SourceId, string?>> games)
    {
        JsonObject section = [];
        foreach ((string game, IReadOnlyDictionary<SourceId, string?> sites) in games.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            JsonObject picked = [];
            foreach ((SourceId site, string? key) in sites.OrderBy(entry => entry.Key.Value, StringComparer.Ordinal))
            {
                picked[site.Value] = key;
            }

            if (picked.Count > 0)
            {
                section[game] = picked;
            }
        }

        Keep(file, InstalledGames, section);
    }

    /// <summary>
    /// Whether a key names an installed game: its store and its ID there, such as steam/489830.
    /// </summary>
    private static bool IsInstalledGame(string key)
        => key.IndexOf('/', StringComparison.Ordinal) is var slash and > 0 && slash < key.Length - 1 && !string.IsNullOrWhiteSpace(key);
}
