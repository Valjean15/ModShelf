using System.CommandLine;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Downloads;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Downloads;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>download &lt;link&gt; --to &lt;folder&gt; [--name &lt;file&gt;]</c>: adds the file to the queue and runs the
/// download engine until that file is done. When the site wants the download started on its website, it shows the
/// page and asks for the link the site hands over. Ctrl+C stops cleanly; running the same command again continues.
/// </summary>
internal static partial class DownloadCommand
{
    public static Command Create(CliEnvironment environment)
    {
        Argument<string> link = new("link")
        {
            Description = "What to download: a file's web link; a mod's or a file's page on a site ModShelf knows, such as Nexus Mods; "
                + "or an nxm:// link from Nexus Mods. To check a plain web link's file against a hash, end the link with "
                + "#sha256=<hex> (or md5, sha1, sha512).",
        };
        Option<DirectoryInfo> to = new("--to")
        {
            Description = "The folder to save the file in. It's created if it doesn't exist.",
            HelpName = "folder",
            Required = true,
        };
        Option<string?> name = new("--name")
        {
            Description = "The name to save the file under. By default, the name the site gives the file, or the name at the end of the link.",
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

        LinkDownload added;
        try
        {
            added = await links.AddAsync(link, targetDirectory, fileName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (LinkProblem(exception) is { } problem)
        {
            string hint = exception is ArgumentException { ParamName: "fileName" } ? " Choose one with --name." : string.Empty;
            await environment.Error.WriteLineAsync(problem.Message + hint).ConfigureAwait(false);
            return problem.ExitCode;
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

        // Watch first and read the job after, so no change from here on is missed, and none from before (such as the page
        // a job waited on before the link it got just now) is taken for news.
        using CancellationTokenSource watching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IAsyncEnumerable<DownloadJobSnapshot> updates = downloads.WatchAsync(watching.Token);
        DownloadJobSnapshot current = (await downloads.ListAsync(cancellationToken).ConfigureAwait(false))
            .Single(listed => listed.Id == job.Id);

        Follow follow = new(
            environment,
            downloads,
            links,
            new HandoffPrompt(environment, services.GetRequiredService<IBrowserLauncher>()),
            current,
            fileName);
        return await follow.RunAsync(updates, watching, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> StoppedAsync(CliEnvironment environment, bool waitingForHandoff = false)
    {
        await environment.Error.WriteLineAsync(waitingForHandoff
                ? "Stopped. The download is still waiting to be started on the site; run the same command again to continue."
                : "Stopped. The partly downloaded file is kept; run the same command again to continue.")
            .ConfigureAwait(false);
        return ExitCodes.Interrupted;
    }

    /// <summary>
    /// What to tell the user when looking up a link went wrong, or null for an error that isn't the user's to act on.
    /// </summary>
    private static (string Message, int ExitCode)? LinkProblem(Exception exception) => exception switch
    {
        UnsupportedLinkException => ("No source can download that link.", ExitCodes.BadInput),
        ArgumentException refused => (refused.MessageForUser(), ExitCodes.BadInput),
        AuthenticationFailedException => (
            "The site needs a linked account to look that link up, or refused the one linked. Link it with account link, "
            + "then run the same command again.",
            ExitCodes.Failed),
        HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone } => (
            "The site has no such mod or file.",
            ExitCodes.BadInput),
        HttpRequestException failed => ($"The site couldn't look that link up: {failed.Message}", ExitCodes.Failed),
        _ => null,
    };

    private static string FailureMessage(DownloadFailureReason? reason) => reason switch
    {
        DownloadFailureReason.LinkUnavailable => "The download failed: the link no longer works, or the server refused it.",
        DownloadFailureReason.HandoffExpired => "The download failed: it wasn't started on the site in time.",
        DownloadFailureReason.Network => "The download failed: the connection kept failing. Run the same command again to retry.",
        DownloadFailureReason.Storage => "The download failed: the file couldn't be saved. The folder may not be writable, the disk may be full, or a file with that name may exist already.",
        DownloadFailureReason.HashMismatch => "The download failed: the file doesn't match the expected hash, so it was deleted.",
        DownloadFailureReason.AccountNeeded => "The download failed: the site needs a linked account, or refused the one linked. Link it with account link, then run the same command again.",
        _ => "The download failed unexpectedly. Set the environment variable Logging__LogLevel__Default=Debug and run it again for details.",
    };
}
