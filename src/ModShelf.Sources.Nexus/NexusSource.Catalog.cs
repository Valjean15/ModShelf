using System.Globalization;
using System.Text.Json;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="IModCatalog"/> half: games, mods, files and changelogs from the v1 API, one request each.
/// </summary>
public sealed partial class NexusSource : IModCatalog
{
    /// <exception cref="ArgumentException">The game isn't Nexus Mods'.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached or refused; with 404 when there's no such game.</exception>
    public async Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
    {
        string domain = NexusLink.ForGame(game);
        NexusGame found = await _api.GetAsync<NexusGame>($"v1/games/{domain}.json", cancellationToken).ConfigureAwait(false);
        return new GameInfo(
            game,
            string.IsNullOrWhiteSpace(found.Name) ? domain : found.Name,
            WebLink(found.NexusmodsUrl) ?? new Uri(Website, domain));
    }

    /// <exception cref="ArgumentException">The mod isn't Nexus Mods'.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached or refused; with 404 when there's no such mod.</exception>
    public async Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        NexusLink mod = NexusLink.ForMod(modRef);
        NexusMod found = await _api.GetAsync<NexusMod>($"{ModPath(mod)}.json", cancellationToken).ConfigureAwait(false);
        return new ModInfo(
            modRef,
            string.IsNullOrWhiteSpace(found.Name) ? $"Mod {mod.ModId}" : found.Name,
            found.Summary,
            found.Author,
            found.Version,
            pageUrl: new Uri(Website, $"{mod.Game}/mods/{mod.ModId}"),
            pictureUrl: WebLink(found.PictureUrl),
            updatedAt: found.UpdatedTimestamp is { } updated ? FromUnixSeconds(updated) : null,
            isAvailable: found.Available != false && (found.Status is null || found.Status == "published"),
            hasAdultContent: found.ContainsAdultContent == true);
    }

    /// <exception cref="ArgumentException">The mod isn't Nexus Mods'.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached or refused; with 404 when there's no such mod.</exception>
    public async Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        NexusLink mod = NexusLink.ForMod(modRef);
        NexusModFiles found = await _api.GetAsync<NexusModFiles>($"{ModPath(mod)}/files.json", cancellationToken).ConfigureAwait(false);
        return [.. found.Files.Select(file => ToModFile(modRef, mod, file))];
    }

    /// <returns>Each version's changes, in the order Nexus Mods lists them; empty when the mod has no changelog.</returns>
    /// <exception cref="ArgumentException">The mod isn't Nexus Mods'.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached or refused; with 404 when there's no such mod.</exception>
    public async Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        NexusLink mod = NexusLink.ForMod(modRef);
        JsonElement found = await _api.GetAsync<JsonElement>($"{ModPath(mod)}/changelogs.json", cancellationToken).ConfigureAwait(false);

        // An object whose names are versions and whose values are lists of changes, such as {"1.2": ["Fixed …"]}.
        // A mod without a changelog answers {}, and sometimes [].
        if (found.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return
        [
            .. found.EnumerateObject().Select(version => new ModChangelog(
                version.Name,
                version.Value.ValueKind == JsonValueKind.Array
                    ? version.Value.EnumerateArray().Where(change => change.ValueKind == JsonValueKind.String).Select(change => change.GetString()!)
                    : [])),
        ];
    }

    private static string ModPath(NexusLink mod) => $"v1/games/{mod.Game}/mods/{mod.ModId}";

    private static ModFile ToModFile(ModRef modRef, NexusLink mod, NexusFile file)
    {
        string fileId = file.FileId.ToString(CultureInfo.InvariantCulture);
        long? size = file.SizeInBytes ?? file.SizeKb * 1024;
        return new ModFile(
            new ModFileRef(modRef, fileId),
            string.IsNullOrWhiteSpace(file.Name) ? file.FileName ?? $"File {fileId}" : file.Name,
            SuggestFileName(file.FileName) ?? fileId,
            file.Version,
            Category(file),
            size is >= 0 ? size : null,
            FromUnixSeconds(file.UploadedTimestamp),
            hashes: [],
            pageUrl: new Uri(Website, $"{mod.Game}/mods/{mod.ModId}?tab=files&file_id={fileId}"));
    }

    /// <summary>
    /// The section of the Files tab, by its name when Nexus Mods gives one, otherwise by its number. A section with
    /// neither known, such as the unnamed category 6, is Unknown.
    /// </summary>
    private static ModFileCategory Category(NexusFile file) => file.CategoryName?.ToUpperInvariant() switch
    {
        "MAIN" => ModFileCategory.Main,
        "UPDATE" => ModFileCategory.Update,
        "OPTIONAL" => ModFileCategory.Optional,
        "OLD_VERSION" => ModFileCategory.OldVersion,
        "MISCELLANEOUS" => ModFileCategory.Miscellaneous,
        "ARCHIVED" => ModFileCategory.Archived,
        null => file.CategoryId switch
        {
            NexusFile.MainCategory => ModFileCategory.Main,
            2 => ModFileCategory.Update,
            3 => ModFileCategory.Optional,
            4 => ModFileCategory.OldVersion,
            5 => ModFileCategory.Miscellaneous,
            _ => ModFileCategory.Unknown,
        },
        _ => ModFileCategory.Unknown,
    };

    private static DateTimeOffset FromUnixSeconds(long seconds)
        => seconds >= 0 && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UnixEpoch;
}
