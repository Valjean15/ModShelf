using System.Globalization;

using ModShelf.Application.Notifications.Ports;
using ModShelf.Application.Resources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Updates;

/// <summary>
/// The notices a background check shows: of the new updates it found, how many there are and which mods they're for,
/// such as "3 new updates" and "SkyUI, SMAPI and 1 more"; and of the mods it found gone from their sites.
/// </summary>
internal static class UpdateNotices
{
    /// <param name="found">The new updates, in the order <see cref="UpdateCheck.Found"/> lists them; at least one.</param>
    /// <param name="library">The library, for the mods' names. A mod it doesn't have goes by its ID.</param>
    public static Notification For(IReadOnlyList<LibraryUpdate> found, IReadOnlyList<LibraryMod> library)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(library);
        if (found.Count == 0)
        {
            throw new ArgumentException("A notice of new updates needs at least one.", nameof(found));
        }

        Dictionary<ModRef, string> names = library.ToDictionary(mod => mod.Mod, mod => mod.Name);
        string[] mods = [.. found.Select(update => update.File.Mod).Distinct().Select(mod => names.GetValueOrDefault(mod) ?? mod.Id)];
        string title = found.Count == 1 ? Strings.OneNewUpdate : Format(Strings.NewUpdates, found.Count);
        return new Notification(title, Named(mods));
    }

    /// <summary>
    /// The notice of the mods a background check found gone from their sites, such as "2 mods left their sites" and
    /// "SkyUI and SMAPI: your copies may be the only ones left."
    /// </summary>
    /// <param name="gone">The mods, in the order <see cref="UpdateCheck.Gone"/> lists them; at least one.</param>
    public static Notification LeftSite(IReadOnlyList<LibraryMod> gone)
    {
        ArgumentNullException.ThrowIfNull(gone);
        if (gone.Count == 0)
        {
            throw new ArgumentException("A notice of mods gone from their sites needs at least one.", nameof(gone));
        }

        string title = gone.Count == 1 ? Strings.OneModLeftSite : Format(Strings.ModsLeftSite, gone.Count);
        return new Notification(title, Format(Strings.LeftSiteMessage, Named([.. gone.Select(mod => mod.Name)])));
    }

    /// <summary>
    /// Mods by their names: one, two, or the first two and how many more.
    /// </summary>
    private static string Named(string[] mods) => mods switch
    {
        [string only] => only,
        [string first, string second] => Format(Strings.TwoMods, first, second),
        _ => Format(Strings.MoreMods, mods[0], mods[1], mods.Length - 2),
    };

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.CurrentCulture, format, values);
}
