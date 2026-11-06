using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A mod site that says what's left of its account's requests, as the test sets it.
/// </summary>
internal sealed class QuotaSource(SourceId id, string displayName) : IApiQuota
{
    public SourceId Id { get; } = id;

    public string DisplayName { get; } = displayName;

    public SiteWords Words => SiteWords.None;

    public ApiQuota? Quota { get; set; }

    /// <summary>
    /// Sets what's left: an hour's allowance and a day's, each renewing at the time given.
    /// </summary>
    public void Left(int hourLeft, int hourLimit, DateTimeOffset hourRenews, int dayLeft, int dayLimit, DateTimeOffset dayRenews)
        => Quota = new ApiQuota(
        [
            new QuotaAllowance(TimeSpan.FromDays(1), dayLimit, dayLeft, dayRenews),
            new QuotaAllowance(TimeSpan.FromHours(1), hourLimit, hourLeft, hourRenews),
        ]);
}
