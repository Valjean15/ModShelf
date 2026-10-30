using ModShelf.Application.Releases;
using ModShelf.Application.Releases.Ports;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// ModShelf's releases as the test sets them: installed or not, the release a look finds, one downloaded before, and
/// failures. A download reports the progress the test gives, or waits for it. It records what it was asked.
/// </summary>
internal sealed class ScriptedAppReleases(bool installed = true) : IAppReleases
{
    public bool IsInstalled { get; } = installed;

    public AppRelease? Downloaded { get; set; }

    /// <summary>
    /// What a look finds; null for nothing newer.
    /// </summary>
    public AppRelease? Newer { get; set; }

    /// <summary>
    /// What the next look throws instead.
    /// </summary>
    public Exception? CheckFailure { get; set; }

    /// <summary>
    /// What the next download throws instead, once it has reported its progress.
    /// </summary>
    public Exception? DownloadFailure { get; set; }

    /// <summary>
    /// What the next restart throws instead.
    /// </summary>
    public Exception? RestartFailure { get; set; }

    /// <summary>
    /// The progress each download reports.
    /// </summary>
    public IReadOnlyList<int> Steps { get; set; } = [25, 100];

    /// <summary>
    /// While set, a download waits for it, once it has reported its progress.
    /// </summary>
    public TaskCompletionSource? Hold { get; set; }

    public int Checks { get; private set; }

    public List<string> Calls { get; } = [];

    public Task<AppRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        Checks++;
        Calls.Add("Check");
        if (!IsInstalled)
        {
            throw new InvalidOperationException("ModShelf isn't installed.");
        }

        if (CheckFailure is { } failure)
        {
            CheckFailure = null;
            throw failure;
        }

        return Task.FromResult(Newer);
    }

    public async Task DownloadAsync(AppRelease release, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        Calls.Add($"Download {release.Version}");
        foreach (int step in Steps)
        {
            progress?.Report(step);
        }

        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }

        if (DownloadFailure is { } failure)
        {
            DownloadFailure = null;
            throw failure;
        }

        Downloaded = release;
    }

    public void RestartToApply(AppRelease release)
    {
        Calls.Add($"Restart {release.Version}");
        if (RestartFailure is { } failure)
        {
            RestartFailure = null;
            throw failure;
        }
    }
}
