using ModShelf.Application.Downloads.Ports;

namespace ModShelf.Infrastructure.Downloads;

/// <summary>
/// Passes progress on to <see cref="ITransferProgress"/> at most once per interval, so a fast download
/// doesn't flood the scheduler with thousands of reports a second.
/// </summary>
internal sealed class ProgressReporter(ITransferProgress progress, TimeProvider timeProvider, TimeSpan interval)
{
    private long _lastReportTimestamp;
    private long _lastReportedBytes = -1;
    private long _latestBytes;

    public void Report(long bytesDownloaded)
    {
        _latestBytes = bytesDownloaded;
        long now = timeProvider.GetTimestamp();
        if (_lastReportedBytes < 0 || timeProvider.GetElapsedTime(_lastReportTimestamp, now) >= interval)
        {
            Send(now);
        }
    }

    /// <summary>
    /// Reports the latest byte count if it hasn't been reported yet.
    /// </summary>
    public void Flush()
    {
        if (_latestBytes != _lastReportedBytes)
        {
            Send(timeProvider.GetTimestamp());
        }
    }

    private void Send(long now)
    {
        progress.Advanced(_latestBytes);
        _lastReportedBytes = _latestBytes;
        _lastReportTimestamp = now;
    }
}
