using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Downloads;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Downloads;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>download &lt;link&gt; --to &lt;folder&gt; [--name &lt;file&gt;]</c>: adds the file to the queue and runs the
/// download engine until that file is done. Ctrl+C stops cleanly; running the same command again continues.
/// </summary>
internal static class DownloadCommand
{
    public static Command Create(CliEnvironment environment)
    {
        Argument<string> link = new("link")
        {
            Description = "The file's web link. To check the file against a hash, end the link with #sha256=<hex> (or md5, sha1, sha512).",
        };
        Option<DirectoryInfo> to = new("--to")
        {
            Description = "The folder to save the file in. It's created if it doesn't exist.",
            HelpName = "folder",
            Required = true,
        };
        Option<string?> name = new("--name")
        {
            Description = "The name to save the file under. By default, the name at the end of the link.",
            HelpName = "file",
        };

        Command command = new("download", "Downloads a file and waits until it's done. Ctrl+C stops the download; run the same command again to continue it.")
        {
            link,
            to,
            name,
        };
        command.SetAction((parsed, cancellationToken)
            => RunAsync(environment, parsed.GetValue(link)!, parsed.GetValue(to)!, parsed.GetValue(name), cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(
        CliEnvironment environment,
        string linkText,
        DirectoryInfo folder,
        string? fileName,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(linkText, UriKind.Absolute, out Uri? link))
        {
            await environment.Error.WriteLineAsync("That isn't a complete web link, such as https://example.com/file.zip.").ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        string targetDirectory = Path.TrimEndingDirectorySeparator(folder.FullName);

        using IHost host = environment.BuildHost(true);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            return await DownloadAsync(environment, host.Services, link, targetDirectory, fileName, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await StoppedAsync(environment).ConfigureAwait(false);
        }
        finally
        {
            // Stopping the scheduler puts the running download back in the queue, keeping its partial file.
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<int> DownloadAsync(
        CliEnvironment environment,
        IServiceProvider services,
        Uri link,
        string targetDirectory,
        string? fileName,
        CancellationToken cancellationToken)
    {
        IDownloadService downloads = services.GetRequiredService<IDownloadService>();
        ILinkDownloads links = services.GetRequiredService<ILinkDownloads>();

        // Watch before adding, so no change to the job is missed.
        IAsyncEnumerable<DownloadJobSnapshot> updates = downloads.WatchAsync(cancellationToken);

        LinkDownload added;
        try
        {
            added = await links.AddAsync(link, targetDirectory, fileName, cancellationToken).ConfigureAwait(false);
        }
        catch (UnsupportedLinkException)
        {
            await environment.Error.WriteLineAsync("No source can download that link.").ConfigureAwait(false);
            return ExitCodes.BadInput;
        }
        catch (ArgumentException exception)
        {
            string hint = exception.ParamName == "fileName" ? " Choose one with --name." : string.Empty;
            await environment.Error.WriteLineAsync(exception.MessageForUser() + hint).ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        DownloadJobSnapshot job = added.Job;
        if (added.AlreadyQueued)
        {
            environment.Output.WriteLine($"Continuing the download of {job.FileName} added earlier.");
            if (job.Status == DownloadJobStatus.Paused)
            {
                await downloads.ResumeAsync(job.Id, cancellationToken).ConfigureAwait(false);
            }
            else if (job.Status == DownloadJobStatus.Failed)
            {
                await downloads.RetryAsync(job.Id, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            environment.Output.WriteLine($"Downloading {job.FileName} to {job.TargetDirectory}");
        }

        ProgressDisplay display = new(environment.Output, interactive: environment.OutputIsTerminal);
        await foreach (DownloadJobSnapshot update in updates.ConfigureAwait(false))
        {
            if (update.Id != job.Id)
            {
                continue;
            }

            display.Show(update);
            switch (update.Status)
            {
                case DownloadJobStatus.Completed:
                    display.Finish();
                    environment.Output.WriteLine($"Saved {Path.Combine(update.TargetDirectory, update.FileName)}");
                    return ExitCodes.Done;

                case DownloadJobStatus.Failed:
                    display.Finish();
                    await environment.Error.WriteLineAsync(FailureMessage(update.FailureReason)).ConfigureAwait(false);
                    return ExitCodes.Failed;

                case DownloadJobStatus.Cancelled:
                    display.Finish();
                    await environment.Error.WriteLineAsync("The download was cancelled.").ConfigureAwait(false);
                    return ExitCodes.Failed;

                default:
                    break;
            }
        }

        // The updates end when Ctrl+C cancels the token, or when the engine stops on its own.
        display.Finish();
        if (cancellationToken.IsCancellationRequested)
        {
            return await StoppedAsync(environment).ConfigureAwait(false);
        }

        await environment.Error.WriteLineAsync("The download engine stopped before the download finished.").ConfigureAwait(false);
        return ExitCodes.Failed;
    }

    private static async Task<int> StoppedAsync(CliEnvironment environment)
    {
        await environment.Error.WriteLineAsync("Stopped. The partly downloaded file is kept; run the same command again to continue.")
            .ConfigureAwait(false);
        return ExitCodes.Interrupted;
    }

    private static string FailureMessage(DownloadFailureReason? reason) => reason switch
    {
        DownloadFailureReason.LinkUnavailable => "The download failed: the link no longer works, or the server refused it.",
        DownloadFailureReason.HandoffExpired => "The download failed: it wasn't started on the site in time.",
        DownloadFailureReason.Network => "The download failed: the connection kept failing. Run the same command again to retry.",
        DownloadFailureReason.Storage => "The download failed: the file couldn't be saved. The folder may not be writable, the disk may be full, or a file with that name may exist already.",
        DownloadFailureReason.HashMismatch => "The download failed: the file doesn't match the expected hash, so it was deleted.",
        _ => "The download failed unexpectedly. Set the environment variable Logging__LogLevel__Default=Debug and run it again for details.",
    };
}
