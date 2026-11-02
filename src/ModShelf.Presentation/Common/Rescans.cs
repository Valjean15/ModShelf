using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Rescanning from the Library screen or a mod's page, a mod or the whole library, and saying what it found.
/// </summary>
internal static class Rescans
{
    /// <summary>
    /// Rescans, then says what it found in a message. The screens show the library's changes as it makes them.
    /// </summary>
    /// <param name="link">The mod's page, or null for the whole library.</param>
    public static async Task RunAsync(ILibraryRescan rescans, IModLibrary library, IDialogService dialogs, Uri? link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rescans);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(dialogs);

        LibraryRescan rescan;
        try
        {
            rescan = await rescans.RescanAsync(link, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            await dialogs.ShowMessageAsync(Strings.RescanFailedTitle, exception.Message, cancellationToken);
            return;
        }

        IReadOnlyList<LibraryMod> mods = await library.ListAsync(cancellationToken);
        await dialogs.ShowMessageAsync(Strings.RescanDoneTitle, Summary(rescan, mods), cancellationToken);
    }

    /// <summary>
    /// What a rescan found, a sentence a line.
    /// </summary>
    /// <param name="library">The library, for the names of the mods whose files couldn't be matched.</param>
    public static string Summary(LibraryRescan rescan, IReadOnlyList<LibraryMod> library)
    {
        ArgumentNullException.ThrowIfNull(rescan);
        ArgumentNullException.ThrowIfNull(library);

        List<string> lines = [];
        void Say(int count, string one, string many)
        {
            if (count > 0)
            {
                lines.Add(Counts.Of(count, one, many));
            }
        }

        Say(rescan.Added.Count, Strings.RescanAddedOne, Strings.RescanAdded);
        Say(rescan.Hashed.Count, Strings.RescanHashedOne, Strings.RescanHashed);
        Say(rescan.Missing.Count, Strings.RescanMissingOne, Strings.RescanMissing);
        Say(rescan.Unmatched.Count, Strings.RescanUnmatchedOne, Strings.RescanUnmatched);
        if (rescan.Failures.Count > 0)
        {
            RescanFailure first = rescan.Failures[0];
            string name = library.FirstOrDefault(mod => mod.Mod == first.Mod)?.Name ?? first.Mod.Id;
            lines.Add(Text.Format(Strings.RescanNotMatched, name, first.Reason));
            Say(rescan.Failures.Count - 1, Strings.RescanNotMatchedMoreOne, Strings.RescanNotMatchedMore);
        }

        return lines.Count == 0 ? Strings.RescanNothingNew : string.Join(Environment.NewLine, lines);
    }
}
