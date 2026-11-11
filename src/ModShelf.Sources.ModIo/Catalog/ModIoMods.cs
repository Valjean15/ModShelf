using System.Globalization;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.ModIo.Api;

namespace ModShelf.Sources.ModIo.Catalog;

/// <summary>
/// A game or a mod as mod.io describes it, in the words <see cref="GameInfo"/> and <see cref="ModInfo"/> have.
/// </summary>
internal static class ModIoMods
{
    /// <summary>
    /// The platform whose files ModShelf asks for: PC games' mods are Windows files, on Linux too.
    /// </summary>
    public const string Platform = "windows";

    /// <summary>
    /// The flag of a mod with explicit content, which ModShelf takes for adult content; alcohol (1), drugs (2) and
    /// violence (4) aren't.
    /// </summary>
    public const int Explicit = 8;

    /// <summary>
    /// A game's categories: the tags of each group of tags its mods can have, in mod.io's order, those only its admins
    /// see left out.
    /// </summary>
    public static IEnumerable<string> Categories(ModIoGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return (game.TagOptions ?? [])
            .Where(group => group is { Hidden: false })
            .SelectMany(group => group.Tags ?? [])
            .OfType<string>();
    }

    public static GameInfo ToGameInfo(GameRef reference, ModIoGame game)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(game);

        return new GameInfo(
            reference,
            string.IsNullOrWhiteSpace(game.Name) ? reference.Key : game.Name.Trim(),
            WebLink(game.ProfileUrl),
            Categories(game),
            coverUrl: WebLink(game.Logo?.Thumb1280 ?? game.Logo?.Original),
            iconUrl: WebLink(game.Icon?.Thumb128 ?? game.Icon?.Original));
    }

    /// <param name="category">
    /// The category searched for, which a mod found has among its tags; null for its first tag. A mod has many tags,
    /// such as its type, its classes and its languages, and mod.io lists them in the order of the game's groups.
    /// </param>
    public static ModInfo ToModInfo(GameRef game, ModIoMod mod, string? category = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mod);

        string id = mod.Id.ToString(CultureInfo.InvariantCulture);
        string[] tags = [.. (mod.Tags ?? []).Select(tag => tag?.Name?.Trim()).OfType<string>().Where(tag => tag.Length > 0)];
        ModIoModfile? file = mod.Modfile is { Id: > 0 } live ? live : null;
        return new ModInfo(
            new ModRef(game, id),
            string.IsNullOrWhiteSpace(mod.Name) ? id : mod.Name.Trim(),
            summary: mod.Summary,
            author: mod.SubmittedBy?.Username,
            version: file?.Version,
            pageUrl: WebLink(mod.ProfileUrl),
            pictureUrl: WebLink(mod.Logo?.Thumb1280 ?? mod.Logo?.Original),
            updatedAt: Time(mod.DateUpdated),
            isAvailable: mod is { Status: 1, Visible: 1 },
            hasAdultContent: IsAdult(mod),
            downloads: mod.Stats?.DownloadsTotal is >= 0 and var downloads ? downloads : null,
            endorsements: mod.Stats?.RatingsPositive is >= 0 and var ratings ? ratings : null,
            thumbnailUrl: WebLink(mod.Logo?.Thumb320),
            category: category is not null && tags.Contains(category, StringComparer.OrdinalIgnoreCase)
                ? tags.First(tag => string.Equals(tag, category, StringComparison.OrdinalIgnoreCase))
                : tags.FirstOrDefault(),
            size: file?.Filesize is > 0 and var size ? size : null,
            publishedAt: Time(mod.DateLive));
    }

    /// <summary>
    /// Whether mod.io marks the mod as having explicit content.
    /// </summary>
    public static bool IsAdult(ModIoMod mod) => (mod.MaturityOption & Explicit) != 0;

    /// <summary>
    /// The mod's live file for Windows: the one mod.io gives with it, or the one it names for Windows.
    /// </summary>
    public static long? LiveFile(ModIoMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        return mod.Modfile is { Id: > 0 } live
            ? live.Id
            : (mod.Platforms ?? []).FirstOrDefault(platform => string.Equals(platform?.Platform, Platform, StringComparison.OrdinalIgnoreCase))?.ModfileLive is > 0 and var id
                ? id
                : null;
    }

    /// <summary>
    /// A time mod.io gives in seconds since 1970, or null when it gives none.
    /// </summary>
    public static DateTimeOffset? Time(long seconds)
        => seconds is > 0 and < 253402300800 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    /// <summary>
    /// An absolute HTTP or HTTPS link, or null for anything else, which the app must never open.
    /// </summary>
    public static Uri? WebLink(string? text)
        => Uri.TryCreate(text, UriKind.Absolute, out Uri? link) && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
            ? link
            : null;
}
