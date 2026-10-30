using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Common;

/// <summary>
/// How the screens that show the library keep up with it.
/// </summary>
internal static class LibraryFeed
{
    /// <summary>
    /// Lists the library, then follows its changes until <paramref name="cancellationToken"/> is cancelled: watching first
    /// and listing after, so no change from then on is missed. <paramref name="listed"/> and <paramref name="changed"/>
    /// run on the UI thread.
    /// </summary>
    public static async Task FollowAsync(
        IModLibrary library,
        IUiThread uiThread,
        Action<IReadOnlyList<LibraryMod>> listed,
        Action<LibraryChange> changed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(changed);

        IAsyncEnumerable<LibraryChange> changes = library.ChangesAsync(cancellationToken);
        IReadOnlyList<LibraryMod> mods = await library.ListAsync(cancellationToken);
        uiThread.Post(() => listed(mods));

        await foreach (LibraryChange change in changes)
        {
            uiThread.Post(() => changed(change));
        }
    }

    /// <summary>
    /// Keeps each mod as the library has it.
    /// </summary>
    public static void Keep(this IDictionary<ModRef, LibraryMod> mods, IEnumerable<LibraryMod> listed)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(listed);

        foreach (LibraryMod mod in listed)
        {
            mods[mod.Mod] = mod;
        }
    }

    /// <summary>
    /// Keeps the mod a change was to as the change left it: as it is now, or gone from the library.
    /// </summary>
    public static void Apply(this IDictionary<ModRef, LibraryMod> mods, LibraryChange change)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(change);

        if (change.After is { } after)
        {
            mods[change.Mod] = after;
        }
        else
        {
            mods.Remove(change.Mod);
        }
    }
}
