using System.Globalization;

using ModShelf.Application.Notifications.Ports;
using ModShelf.Application.Resources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Updates;

/// <summary>
/// The notice of the new updates a background check found: how many there are, and which mods they're for, such as
/// "3 new updates" and "SkyUI, SMAPI and 1 more".
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
        string message = mods switch
        {
            [string only] => only,
            [string first, string second] => Format(Strings.TwoMods, first, second),
            _ => Format(Strings.MoreMods, mods[0], mods[1], mods.Length - 2),
        };
        return new Notification(title, message);
    }

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.CurrentCulture, format, values);
}
