using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Presentation.ViewModels.ModPages;

/// <summary>
/// What Browse writes of a mod from what its site says, each part empty when the site doesn't say.
/// </summary>
/// <param name="Author">Such as "by schlangster".</param>
/// <param name="Summary">A line or two about the mod.</param>
/// <param name="Version">Such as "Version 5.2SE".</param>
/// <param name="Updated">Such as "Updated 10/27/2026", in the user's time zone and culture.</param>
/// <param name="Downloads">Such as "2,134,567 downloads".</param>
/// <param name="Endorsements">Such as "98,765 endorsements", in the site's words; empty when it has none for them.</param>
internal sealed record ModInfoText(string Author, string Summary, string Version, string Updated, string Downloads, string Endorsements)
{
    /// <param name="words">The mod's site's words, for what it counts as approval.</param>
    public static ModInfoText Of(ModInfo mod, SiteWords words, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ModInfoText(
            mod.Author is { } author ? Text.Format(Strings.ModByAuthor, author) : string.Empty,
            mod.Summary ?? string.Empty,
            mod.Version is { } version ? Text.Format(Strings.FileVersion, version) : string.Empty,
            mod.UpdatedAt is { } updated ? Text.Format(Strings.BrowseUpdated, Dates.Day(updated, timeProvider)) : string.Empty,
            mod.Downloads is { } downloads ? Counts.Of(downloads, Strings.BrowseOneDownload, Strings.BrowseDownloads) : string.Empty,
            Approvals(mod, words));
    }

    /// <summary>
    /// Such as "98,765 endorsements"; empty when the site doesn't say, or has no words for them.
    /// </summary>
    private static string Approvals(ModInfo mod, SiteWords words)
    {
        if (mod.Endorsements is not { } count || words.Approval is not { } one || words.Approvals is not { } many)
        {
            return string.Empty;
        }

        return count == 1 ? Text.Format(Strings.BrowseOneApproval, one) : Text.Format(Strings.BrowseApprovals, count, many);
    }
}
