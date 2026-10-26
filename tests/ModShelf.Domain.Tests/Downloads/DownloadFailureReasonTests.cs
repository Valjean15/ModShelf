using ModShelf.Domain.Downloads;

namespace ModShelf.Domain.Tests.Downloads;

public sealed class DownloadFailureReasonTests
{
    /// <summary>
    /// The library database stores the numbers, so a renumbered reason would change the meaning of saved jobs.
    /// </summary>
    [Fact]
    public void StoredNumbersNeverChange()
    {
        Dictionary<DownloadFailureReason, int> stored = new()
        {
            [DownloadFailureReason.LinkUnavailable] = 1,
            [DownloadFailureReason.HandoffExpired] = 2,
            [DownloadFailureReason.Network] = 3,
            [DownloadFailureReason.Storage] = 4,
            [DownloadFailureReason.HashMismatch] = 5,
            [DownloadFailureReason.Unexpected] = 6,
            [DownloadFailureReason.AccountNeeded] = 7,
        };

        Assert.Equal(stored.Keys.Order(), Enum.GetValues<DownloadFailureReason>().Order());
        Assert.All(stored, pair => Assert.Equal(pair.Value, (int)pair.Key));
    }
}
