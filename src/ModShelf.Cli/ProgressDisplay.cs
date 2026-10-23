using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;

namespace ModShelf.Cli;

/// <summary>
/// Shows one job's progress: on a terminal, a single line that updates in place; when the output is
/// redirected, a line per status change and per tenth of the file, so logs stay readable.
/// </summary>
internal sealed class ProgressDisplay(TextWriter output, bool interactive)
{
    private DownloadJobStatus? _lastStatus;
    private long _lastTenth = -1;
    private int _lineLength;

    public void Show(DownloadJobSnapshot job)
    {
        string line = Describe(job);
        bool newTenth = NewTenth(job);
        if (interactive)
        {
            output.Write($"\r{line.PadRight(_lineLength)}");
            _lineLength = line.Length;
        }
        else if (job.Status != _lastStatus || newTenth)
        {
            output.WriteLine(line);
        }

        _lastStatus = job.Status;
    }

    /// <summary>
    /// Ends the in-place line, so whatever comes next starts on a line of its own.
    /// </summary>
    public void Finish()
    {
        if (interactive && _lineLength > 0)
        {
            output.WriteLine();
            _lineLength = 0;
        }
    }

    private static string Describe(DownloadJobSnapshot job) => job.Status switch
    {
        DownloadJobStatus.Queued => "Waiting for a free download slot",
        DownloadJobStatus.Resolving => "Getting the download link",
        DownloadJobStatus.AwaitingHandoff => "Waiting for the download to be started on the site",
        DownloadJobStatus.Downloading => $"Downloading {Sizes.Progress(job.BytesDownloaded, job.TotalBytes)}",
        DownloadJobStatus.Verifying => "Checking the file",
        DownloadJobStatus.Completed => "Done",
        DownloadJobStatus.Paused => "Paused",
        DownloadJobStatus.Failed => "Failed",
        DownloadJobStatus.Cancelled => "Cancelled",
        _ => job.Status.ToString(),
    };

    private bool NewTenth(DownloadJobSnapshot job)
    {
        if (job.Status != DownloadJobStatus.Downloading || job.TotalBytes is not > 0)
        {
            return false;
        }

        long tenth = 10 * job.BytesDownloaded / job.TotalBytes.Value;
        bool isNew = tenth > _lastTenth;
        _lastTenth = tenth;
        return isNew;
    }
}
