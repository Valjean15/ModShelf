using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;
using ModShelf.Application.Sources;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Downloads;
using ModShelf.Infrastructure.Logging;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>download &lt;link&gt; [--to &lt;folder&gt; [--name &lt;file&gt;]] [--with-requirements [--deep] [--yes]]</c>: adds the
/// file to the queue and runs the download engine until that file is done. Without <c>--to</c>, the file goes into the
/// library and is recorded there; with <c>--with-requirements</c>, so do the mods it needs that the user agrees to.
/// When the site wants the download started on its website, it shows the page and asks for the link the site hands
/// over. Ctrl+C stops cleanly; running the same command again continues.
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
        Option<DirectoryInfo?> to = new("--to")
        {
            Description = "The folder to save the file in, outside the library. It's created if it doesn't exist. Without --to, "
                + "the file goes into the library: under its root, in the folder its game's pattern gives (see settings show).",
            HelpName = "folder",
        };
        Option<string?> name = new("--name")
        {
            Description = "With --to, the name to save the file under. By default, the name the site gives the file, or the "
                + "name at the end of the link.",
            HelpName = "file",
        };

        Option<bool> withRequirements = new("--with-requirements")
        {
            Description = "Also download the mods it needs that the library lacks, as its site lists them, each by its newest main "
                + "file. Without --yes, it only lists them. Things off the site, such as a script extender, are only pointed to.",
        };
        Option<bool> deep = new("--deep")
        {
            Description = "With --with-requirements, also list what those mods need, down to three levels, each mod once.",
        };
        Option<bool> yes = new("--yes")
        {
            Description = "With --with-requirements, download the mods it needs too, rather than only listing them.",
        };

        Command command = new(
            "download",
            "Downloads a file into the library, or into a folder with --to, and waits until it's done. Ctrl+C stops the "
                + "download; run the same command again to continue it.")
        {
            link,
            to,
            name,
            withRequirements,
            deep,
            yes,
        };
        command.SetAction((parsed, cancellationToken)
            => RunAsync(
                environment,
                parsed.GetValue(link)!,
                parsed.GetValue(to),
                parsed.GetValue(name),
                new Requiring(parsed.GetValue(withRequirements), parsed.GetValue(deep), parsed.GetValue(yes)),
                cancellationToken));
        return command;
    }

    private static async Task<int> RunAsync(
        CliEnvironment environment,
        string linkText,
        DirectoryInfo? folder,
        string? fileName,
        Requiring requiring,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(linkText, UriKind.Absolute, out Uri? link))
        {
            await environment.Error.WriteLineAsync("That isn't a complete web link, such as https://example.com/file.zip.").ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        if (folder is null && fileName is not null)
        {
            await environment.Error.WriteLineAsync(
                    "--name works with --to only: files in the library keep the names their sites give them.")
                .ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        if (!requiring.With && (requiring.Deep || requiring.Yes))
        {
            await environment.Error.WriteLineAsync("--deep and --yes work with --with-requirements only.").ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        if (requiring.With && folder is not null)
        {
            await environment.Error.WriteLineAsync(
                    "--with-requirements downloads into the library only: leave out --to.")
                .ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        string? targetDirectory = folder is null ? null : Path.TrimEndingDirectorySeparator(folder.FullName);
        if (targetDirectory is not null && File.Exists(targetDirectory))
        {
            await environment.Error.WriteLineAsync($"{targetDirectory} is a file. Give --to a folder; it's created if it doesn't exist.")
                .ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        // In cmd and Windows PowerShell, a quoted folder that ends in a backslash, such as "D:\My Mods\", reaches the command
        // with the closing quote in it; no Windows name holds one.
        if (targetDirectory is not null && OperatingSystem.IsWindows() && targetDirectory.Contains('"', StringComparison.Ordinal))
        {
            await environment.Error.WriteLineAsync(
                    $"{targetDirectory} isn't a folder Windows can have: a name can't hold a quote. If you quoted a folder that ends in "
                    + "a backslash, leave the backslash out, such as \"D:\\My Mods\".")
                .ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        using IHost host = environment.BuildHost(true);
        bool started = false;
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            started = true;
            return await DownloadAsync(environment, host.Services, link, targetDirectory, fileName, requiring, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return started
                ? await StoppedAsync(environment).ConfigureAwait(false)
                : await NothingAddedAsync(environment).ConfigureAwait(false);
        }
        finally
        {
            // Stopping the scheduler puts the running download back in the queue, keeping its partial file.
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <param name="targetDirectory">The folder given with --to; or null for the library.</param>
    private static async Task<int> DownloadAsync(
        CliEnvironment environment,
        IServiceProvider services,
        Uri link,
        string? targetDirectory,
        string? fileName,
        Requiring requiring,
        CancellationToken cancellationToken)
    {
        IDownloadService downloads = services.GetRequiredService<IDownloadService>();
        ILinkDownloads links = services.GetRequiredService<ILinkDownloads>();
        ILibraryDownloads? library = targetDirectory is null ? services.GetRequiredService<ILibraryDownloads>() : null;

        // Before anything is added: the process running the queue wouldn't start a job added here.
        if (downloads.IsQueueBusyElsewhere)
        {
            await environment.Error.WriteLineAsync(LinkProblems.QueueBusyElsewhere).ConfigureAwait(false);
            return ExitCodes.Failed;
        }

        List<DownloadJobSnapshot> required = [];
        bool requirementsFailed = false;
        if (requiring.With)
        {
            switch (await RequirementsAsync(environment, services, link, requiring, cancellationToken).ConfigureAwait(false))
            {
                case { ExitCode: { } exitCode }:
                    return exitCode;
                case { Jobs: var jobs, Failed: var failed }:
                    required.AddRange(jobs);
                    requirementsFailed = failed;
                    break;
            }
        }

        LinkDownload added;
        try
        {
            if (library is null)
            {
                added = await links.AddAsync(link, targetDirectory!, fileName, cancellationToken).ConfigureAwait(false);
                if (added.AlreadySaved)
                {
                    environment.Output.WriteLine($"{Path.Combine(added.Job.TargetDirectory, added.Job.FileName)} is saved already.");
                    return ExitCodes.Done;
                }
            }
            else
            {
                LibraryDownload placed = await library.AddAsync(link, cancellationToken).ConfigureAwait(false);
                if (placed.Queued is null)
                {
                    environment.Output.WriteLine($"{placed.Path} is in the library already.");
                    return required.Count == 0
                        ? requirementsFailed ? ExitCodes.Failed : ExitCodes.Done
                        : Worst(await FollowEachAsync(environment, services, library, required, cancellationToken).ConfigureAwait(false), requirementsFailed);
                }

                added = placed.Queued;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl+C while the link was looked up: no download was added.
            return await NothingAddedAsync(environment).ConfigureAwait(false);
        }
        catch (Exception exception) when (Problem(exception, library is not null) is { } problem)
        {
            string hint = exception is ArgumentException { ParamName: "fileName" } ? " Choose one with --name." : string.Empty;
            await environment.Error.WriteLineAsync(problem.Message + hint).ConfigureAwait(false);
            return problem.ExitCode;
        }

        // A job added earlier goes on: adding its link again resumed it when it was paused, and retried it when it failed.
        DownloadJobSnapshot job = added.Job;
        if (added.AlreadyQueued)
        {
            environment.Output.WriteLine($"Continuing the download of {job.FileName} added earlier.");
        }
        else
        {
            environment.Output.WriteLine($"Downloading {job.FileName} to {job.TargetDirectory}");
        }

        int ended = await FollowOneAsync(environment, services, library, job, cancellationToken).ConfigureAwait(false);
        return required.Count == 0 || ended != ExitCodes.Done
            ? Worst(ended, requirementsFailed)
            : Worst(await FollowEachAsync(environment, services, library!, required, cancellationToken).ConfigureAwait(false), requirementsFailed);
    }

    /// <summary>
    /// Follows one job until it's done, as the command does its file.
    /// </summary>
    private static async Task<int> FollowOneAsync(
        CliEnvironment environment,
        IServiceProvider services,
        ILibraryDownloads? library,
        DownloadJobSnapshot job,
        CancellationToken cancellationToken)
    {
        IDownloadService downloads = services.GetRequiredService<IDownloadService>();

        // Watch first and read the job after, so no change from here on is missed, and none from before (such as the page
        // a job waited on before the link it got just now) is taken for news.
        using CancellationTokenSource watching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IAsyncEnumerable<DownloadJobSnapshot> updates = downloads.WatchAsync(watching.Token);
        DownloadJobSnapshot current = (await downloads.ListAsync(cancellationToken).ConfigureAwait(false))
            .Single(listed => listed.Id == job.Id);

        bool handsOverLinks = services.GetRequiredService<ISourceRegistry>().Find<IDownloadResolver>(job.File.Mod.Game.Source)?.HandsOverLinks ?? true;
        string browserFolder = await BrowserFolderAsync(services, cancellationToken).ConfigureAwait(false);
        Follow follow = new(
            environment,
            downloads,
            services.GetRequiredService<ILinkDownloads>(),
            library,
            new HandoffPrompt(environment, services.GetRequiredService<IBrowserLauncher>(), handsOverLinks, browserFolder),
            current,
            services.GetService<LogFolder>()?.Path);
        return await follow.RunAsync(updates, watching, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Follows the requirements' downloads in turn, after the file's; one that fails doesn't stop the others.
    /// </summary>
    /// <returns>The worst way one ended: <see cref="ExitCodes.Done"/> when they all did.</returns>
    private static async Task<int> FollowEachAsync(
        CliEnvironment environment,
        IServiceProvider services,
        ILibraryDownloads library,
        IReadOnlyList<DownloadJobSnapshot> jobs,
        CancellationToken cancellationToken)
    {
        int worst = ExitCodes.Done;
        foreach (DownloadJobSnapshot job in jobs)
        {
            environment.Output.WriteLine($"Downloading {job.FileName}, which it needs, to {job.TargetDirectory}");
            int ended = await FollowOneAsync(environment, services, library, job, cancellationToken).ConfigureAwait(false);
            if (ended == ExitCodes.Interrupted)
            {
                return ended;
            }

            if (ended != ExitCodes.Done)
            {
                worst = ExitCodes.Failed;
            }
        }

        return worst;
    }

    private static int Worst(int exitCode, bool requirementsFailed) => exitCode == ExitCodes.Done && requirementsFailed ? ExitCodes.Failed : exitCode;

    /// <summary>
    /// The folder the browser saves downloads to: as set, or the system's Downloads folder, also when the settings can't
    /// be read.
    /// </summary>
    private static async Task<string> BrowserFolderAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        string systems = services.GetRequiredService<IUserFolders>().Downloads;
        try
        {
            return (await services.GetRequiredService<ISettingsService>().GetAsync(cancellationToken).ConfigureAwait(false)).BrowserDownloadsFolder ?? systems;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return systems;
        }
    }

    private static async Task<int> NothingAddedAsync(CliEnvironment environment)
    {
        await environment.Error.WriteLineAsync("Stopped before anything was added.").ConfigureAwait(false);
        return ExitCodes.Interrupted;
    }

    private static async Task<int> StoppedAsync(CliEnvironment environment, bool waitingForHandoff = false)
    {
        await environment.Error.WriteLineAsync(waitingForHandoff
                ? "Stopped. The download is still waiting to be started on the site; run the same command again to continue."
                : "Stopped. What's downloaded so far is kept; run the same command again to continue.")
            .ConfigureAwait(false);
        return ExitCodes.Interrupted;
    }

    /// <summary>
    /// What to tell the user when adding the link went wrong, or null for an error that isn't the user's to act on.
    /// </summary>
    /// <param name="library">Whether the file was to go into the library.</param>
    private static (string Message, int ExitCode)? Problem(Exception exception, bool library) => exception switch
    {
        // The library wraps the refusal of every source; its own refusal is of a link to a site without a catalog.
        UnsupportedLinkException { InnerException: not UnsupportedLinkException } when library => (
            "Only the files of mods on sites such as Nexus Mods go into the library. To download this file into a folder of "
            + "your own, add --to <folder>.",
            ExitCodes.BadInput),
        IOException when library => ($"{exception.Message} See where files go with settings show.", ExitCodes.Failed),
        InvalidDataException => (exception.Message, ExitCodes.Failed),
        _ => LinkProblems.Describe(exception, unsupported: "No source can download that link."),
    };

    /// <param name="logFolder">Where the log files are, which hold the details of an unexpected failure.</param>
    private static string FailureMessage(DownloadFailureReason? reason, string? logFolder) => reason switch
    {
        DownloadFailureReason.LinkUnavailable => "The download failed: the link no longer works, or the server refused it.",
        DownloadFailureReason.HandoffExpired => "The download failed: it wasn't started on the site in time.",
        DownloadFailureReason.Network => "The download failed: the connection kept failing. Run the same command again to retry.",
        DownloadFailureReason.Storage => "The download failed: the file couldn't be saved. The folder may not be writable, the disk may be full, or a file with that name may exist already.",
        DownloadFailureReason.HashMismatch => "The download failed: the file doesn't match the expected hash, so it was deleted.",
        DownloadFailureReason.AccountNeeded => "The download failed: the site needs a linked account, or refused the one linked. Link it with account link, then run the same command again.",
        DownloadFailureReason.RateLimited => "The download failed: the site's rate limit for this account is used up. Run the same command again once it resets; account check says when.",
        _ when logFolder is not null => $"The download failed unexpectedly. The details are in the log in {logFolder}.",
        _ => "The download failed unexpectedly.",
    };
}
