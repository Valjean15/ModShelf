using System.Diagnostics;
using System.Globalization;

using ModShelf.Platform.Notifications;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Notifications;

/// <summary>
/// Real processes, through <c>/bin/sh</c>, so these run on macOS and Linux, where notices start one.
/// </summary>
public sealed class ProcessResultTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunsToTheEndAndKeepsWhatItWroteToStandardError()
    {
        SkipOnWindows();

        ProcessResult result = await ProcessResult.RunAsync(Shell("echo oops >&2; exit 3"), TimeSpan.FromSeconds(30), Cancellation);

        Assert.Equal(new ProcessResult(3, "oops\n"), result);
    }

    [Fact]
    public async Task StopsAProcessThatRunsTooLong()
    {
        SkipOnWindows();
        using TempFolder folder = new();
        string idFile = Path.Combine(folder.Path, "pid");
        Stopwatch elapsed = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(
            () => ProcessResult.RunAsync(Shell($"echo $$ > '{idFile}'; sleep 30"), TimeSpan.FromMilliseconds(500), Cancellation));

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(20), $"It took {elapsed.Elapsed}.");
        int id = int.Parse(File.ReadAllText(idFile).Trim(), CultureInfo.InvariantCulture);
        Assert.True(await HasEndedAsync(id), $"Process {id} still runs.");
    }

    [Fact]
    public async Task StopsAProcessWhenCancelled()
    {
        SkipOnWindows();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessResult.RunAsync(Shell("sleep 30"), TimeSpan.FromSeconds(30), cancellation.Token));
    }

    /// <summary>
    /// Whether the process ends within a few seconds; a stopped one may take a moment to go.
    /// </summary>
    private static async Task<bool> HasEndedAsync(int id)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                using Process process = Process.GetProcessById(id);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // No process has the ID now.
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), Cancellation);
        }

        return false;
    }

    private static void SkipOnWindows() => Assert.SkipWhen(OperatingSystem.IsWindows(), "Notices start processes only on macOS and Linux.");

    private static ProcessStartInfo Shell(string command)
    {
        ProcessStartInfo info = new("/bin/sh") { UseShellExecute = false, RedirectStandardError = true };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(command);
        return info;
    }
}
