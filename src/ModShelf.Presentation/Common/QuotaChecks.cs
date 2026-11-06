using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Asks before a big check or download that would likely use up a site's requests, and offers to go on.
/// </summary>
internal static class QuotaChecks
{
    /// <summary>
    /// The most requests a download into the library takes from its site: the mod's files, then the file's link.
    /// </summary>
    public const int PerDownload = 2;

    /// <summary>
    /// Whether to go on with work that takes up to so many requests from each site: at once while every site has as many
    /// left in its tightest allowance, or as its site doesn't say; otherwise as the user answers, for the first site that
    /// hasn't.
    /// </summary>
    /// <param name="needs">Each site and the requests a part of the work takes from it at most; a site may come more than once.</param>
    public static async Task<bool> GoOnAsync(
        ISourceRegistry sources,
        IDialogService dialogs,
        TimeProvider timeProvider,
        IEnumerable<(SourceId Source, int Requests)> needs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(needs);

        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (IGrouping<SourceId, (SourceId Source, int Requests)> site in needs.GroupBy(need => need.Source))
        {
            int requests = site.Sum(need => need.Requests);
            if (sources.Find<IApiQuota>(site.Key)?.Quota is { } quota
                && Allowances.Tightest(quota, now) is { } allowance
                && requests > allowance.Remaining)
            {
                string left = Text.Format(
                    Allowances.Of(allowance, Strings.QuotaLeftThisHour, Strings.QuotaLeftToday, Strings.QuotaLeft),
                    allowance.Remaining);
                string message = Text.Format(
                    Strings.QuotaAskMessage,
                    sources.SiteName(site.Key),
                    requests,
                    left,
                    Allowances.RenewsAt(allowance, timeProvider));
                return await dialogs.ConfirmAsync(Strings.QuotaAskTitle, message, Strings.QuotaAskGoOn, cancellationToken);
            }
        }

        return true;
    }
}
