using System.Globalization;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Links;

namespace ModShelf.Sources.GameBanana.Catalog;

/// <summary>
/// A mod as GameBanana describes it, in the words <see cref="ModInfo"/> has: its profile, or an entry of a list.
/// </summary>
internal static class GameBananaMods
{
    /// <summary>
    /// How GameBanana shows a mod rated for adults: behind a warning, or hidden until asked.
    /// </summary>
    private static readonly string[] AdultVisibilities = ["warn", "hide"];

    /// <param name="category">The category to give it: its top one, in a list, or its own, in its profile.</param>
    public static ModInfo ToModInfo(GameRef game, GameBananaMod mod, string? category)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mod);

        string id = mod.Id.ToString(CultureInfo.InvariantCulture);
        GameBananaImage? picture = mod.PreviewMedia?.Images?.FirstOrDefault(image => image is not null);
        return new ModInfo(
            new ModRef(game, id),
            string.IsNullOrWhiteSpace(mod.Name) ? id : mod.Name.Trim(),
            summary: mod.Description,
            author: mod.Submitter?.Name,
            version: mod.Version,
            pageUrl: WebLink(mod.ProfileUrl) ?? GameBananaLink.ModPage(id),
            pictureUrl: Picture(picture, picture?.File530 ?? picture?.File),
            updatedAt: Time(mod.DateModified),
            isAvailable: !mod.IsTrashed && !mod.IsWithheld && !mod.IsPrivate,
            hasAdultContent: IsAdult(mod.InitialVisibility),
            downloads: Count(mod.DownloadCount),
            endorsements: Count(mod.LikeCount),
            thumbnailUrl: Picture(picture, picture?.File220 ?? picture?.File),
            category: category,
            publishedAt: Time(mod.DateAdded));
    }

    /// <summary>
    /// One of a game's top submissions, a mod, as <see cref="ModInfo"/> has it; what GameBanana says of it there.
    /// </summary>
    public static ModInfo ToModInfo(GameRef game, GameBananaTopMod mod)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mod);

        string id = mod.Id.ToString(CultureInfo.InvariantCulture);
        return new ModInfo(
            new ModRef(game, id),
            string.IsNullOrWhiteSpace(mod.Name) ? id : mod.Name.Trim(),
            author: mod.Submitter?.Name,
            pageUrl: WebLink(mod.ProfileUrl) ?? GameBananaLink.ModPage(id),
            pictureUrl: WebLink(mod.ImageUrl),
            hasAdultContent: IsAdult(mod.InitialVisibility),
            endorsements: Count(mod.LikeCount),
            thumbnailUrl: WebLink(mod.ThumbnailUrl));
    }

    /// <summary>
    /// Whether GameBanana shows a mod rated for adults behind a warning, or hides it until asked.
    /// </summary>
    public static bool IsAdult(string? visibility)
        => visibility is not null && AdultVisibilities.Contains(visibility.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A time GameBanana gives in seconds since 1970, or null when it gives none.
    /// </summary>
    public static DateTimeOffset? Time(long? seconds)
        => seconds is > 0 and < 253402300800 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : null;

    /// <summary>
    /// An absolute HTTP or HTTPS link, or null for anything else, which the app must never open.
    /// </summary>
    public static Uri? WebLink(string? text)
        => Uri.TryCreate(text, UriKind.Absolute, out Uri? link) && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
            ? link
            : null;

    private static Uri? Picture(GameBananaImage? image, string? file)
        => image?.BaseUrl is { } folder && !string.IsNullOrWhiteSpace(file) ? WebLink($"{folder.TrimEnd('/')}/{file.Trim()}") : null;

    private static long? Count(long? count) => count is >= 0 ? count : null;
}
