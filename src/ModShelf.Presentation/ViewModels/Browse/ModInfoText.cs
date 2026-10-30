using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// What Browse writes of a mod from what its site says, each part empty when the site doesn't say.
/// </summary>
/// <param name="Author">Such as "by schlangster".</param>
/// <param name="Summary">A line or two about the mod.</param>
/// <param name="Version">Such as "Version 5.2SE".</param>
/// <param name="Updated">Such as "Updated 10/27/2026", in the user's time zone and culture.</param>
/// <param name="Downloads">Such as "2,134,567 downloads".</param>
/// <param name="Endorsements">Such as "98,765 endorsements".</param>
internal sealed record ModInfoText(string Author, string Summary, string Version, string Updated, string Downloads, string Endorsements)
{
    public static ModInfoText Of(ModInfo mod, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ModInfoText(
            mod.Author is { } author ? Text.Format(Strings.LibraryByAuthor, author) : string.Empty,
            mod.Summary ?? string.Empty,
            mod.Version is { } version ? Text.Format(Strings.LibraryFileVersion, version) : string.Empty,
            mod.UpdatedAt is { } updated ? Text.Format(Strings.BrowseUpdated, Dates.Day(updated, timeProvider)) : string.Empty,
            mod.Downloads is { } downloads ? Counts.Of(downloads, Strings.BrowseOneDownload, Strings.BrowseDownloads) : string.Empty,
            mod.Endorsements is { } endorsements ? Counts.Of(endorsements, Strings.BrowseOneEndorsement, Strings.BrowseEndorsements) : string.Empty);
    }
}
