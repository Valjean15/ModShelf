using System.Diagnostics;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// How a process that shows a notice ended: its exit code, and what it wrote to standard error.
/// </summary>
internal sealed record ProcessResult(int ExitCode, string Errors)
{
    /// <summary>
    /// Runs the process to its end. One that runs past <paramref name="timeout"/>, or is cancelled, is stopped.
    /// </summary>
    /// <param name="info">The process, with standard error redirected.</param>
    /// <exception cref="TimeoutException">It ran past <paramref name="timeout"/>.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">It couldn't start.</exception>
    public static async Task<ProcessResult> RunAsync(ProcessStartInfo info, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);

        using Process process = Process.Start(info) ?? throw new InvalidOperationException($"{info.FileName} didn't start.");
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            Task<string> errors = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await errors.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It ended just now.
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{info.FileName} didn't finish within {timeout.TotalSeconds:0} seconds, so it was stopped.");
        }
    }
}
