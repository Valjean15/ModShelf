using System.CommandLine;
using System.Globalization;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Lists;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Cli.Common;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>library export [--to &lt;file&gt;] [--game &lt;game&gt;]</c> and
/// <c>library import &lt;file-or-link&gt; [--watch-only] [--optional] [--newest-for-missing] [--yes]</c>: the library as
/// a list of mods and their files, and such a list, or a site's collection, brought into the library (ADR 0098).
/// </summary>
internal static partial class LibraryCommand
{
    /// <summary>
    /// The largest list file read, far more than a library of thousands of mods takes.
    /// </summary>
    private const long MaxListBytes = 64L * 1024 * 1024;

    private static Command CreateExport(CliEnvironment environment)
    {
        Option<FileInfo?> to = new("--to")
        {
            Description = "The file to write the list to, replacing one there. Without it, the list is written to the output.",
        };
        Option<string?> game = new("--game")
        {
            Description = "Only this game's mods, by its site and its key there, such as nexus/stardewvalley; its key alone does while only one site's game has it.",
        };
        Command command = new("export", "Writes the library as a list of its mods and their files, by the sites' IDs, which library import reads back here or on another computer.")
        {
            to,
            game,
        };
        command.SetAction((parsed, cancellationToken)
            => ExportAsync(environment, parsed.GetValue(to)?.FullName, parsed.GetValue(game), cancellationToken));
        return command;
    }

    private static Command CreateImport(CliEnvironment environment)
    {
        Argument<string> list = new("file-or-link")
        {
            Description = "A list library export wrote, or the link of a collection on its site, such as a Nexus Mods collection's page.",
        };
        Option<bool> watchOnly = new("--watch-only")
        {
            Description = "Only watch the list's mods; download nothing.",
        };
        Option<bool> optional = new("--optional")
        {
            Description = "Also download the files the list or collection marks optional.",
        };
        Option<bool> newest = new("--newest-for-missing")
        {
            Description = "For a listed file its site no longer offers, download the mod's newest main file instead.",
        };
        Option<bool> yes = new("--yes")
        {
            Description = "Import the list. Without it, the command only says what importing would do.",
        };
        Command command = new("import", "Brings a list of mods, or a collection's, into the library: watches the mods new to it, and queues the listed files it lacks. Never installs anything. Says what it would do unless --yes is given.")
        {
            list,
            watchOnly,
            optional,
            newest,
            yes,
        };
        command.SetAction((parsed, cancellationToken) => ImportAsync(
            environment,
            parsed.GetValue(list)!,
            new ListImportOptions(!parsed.GetValue(watchOnly), parsed.GetValue(optional), parsed.GetValue(newest)),
            parsed.GetValue(yes),
            cancellationToken));
        return command;
    }

    private static async Task<int> ExportAsync(CliEnvironment environment, string? path, string? gameName, CancellationToken cancellationToken)
    {
        try
        {
            return await CommandHost.RunAsync(
                    environment,
                    async services =>
                    {
                        GameRef? game = gameName is null
                            ? null
                            : await SettingChanges.GameNamedAsync(
                                    gameName,
                                    services.GetRequiredService<ISettingsService>(),
                                    services.GetRequiredService<IModLibrary>(),
                                    services.GetRequiredService<ISourceRegistry>(),
                                    cancellationToken)
                                .ConfigureAwait(false);
                        IModLists lists = services.GetRequiredService<IModLists>();
                        ModList list = await lists.ExportAsync(new ListScope(game), cancellationToken).ConfigureAwait(false);
                        string text = lists.Write(list);
                        if (path is null)
                        {
                            environment.Output.WriteLine(text);
                            return ExitCodes.Done;
                        }

                        await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
                        int files = list.Mods.Sum(mod => mod.Files.Count);
                        environment.Output.WriteLine(string.Create(
                            CultureInfo.CurrentCulture,
                            $"Wrote {Count(list.Mods.Count, "mod", "mods")} and {Count(files, "file", "files")} to {path}."));
                        return ExitCodes.Done;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            await environment.Error.WriteLineAsync(exception.MessageForUser()).ConfigureAwait(false);
            return ExitCodes.BadInput;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.Failed;
        }
    }

    private static async Task<int> ImportAsync(
        CliEnvironment environment,
        string source,
        ListImportOptions options,
        bool yes,
        CancellationToken cancellationToken)
    {
        Uri? link = Uri.TryCreate(source, UriKind.Absolute, out Uri? given) && !given.IsFile && !given.IsUnc ? given : null;
        string? text = null;
        if (link is null)
        {
            try
            {
                string path = Path.GetFullPath(source);
                if (new FileInfo(path).Length > MaxListBytes)
                {
                    await environment.Error.WriteLineAsync($"{path} is too large to be a ModShelf list.").ConfigureAwait(false);
                    return ExitCodes.BadInput;
                }

                text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                return ExitCodes.BadInput;
            }
        }

        // Only queuing files needs the queue run here: the process running it wouldn't start a job added here.
        using IHost host = environment.BuildHost(yes && options.Download);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            IServiceProvider services = host.Services;
            IModLists lists = services.GetRequiredService<IModLists>();
            (ModList? list, int refused) = text is not null
                ? (lists.Read(text), ExitCodes.Done)
                : await ReadCollectionAsync(environment, lists, link!, cancellationToken).ConfigureAwait(false);
            if (list is null)
            {
                return refused;
            }

            ListImportPlan plan = await lists.PlanAsync(list, cancellationToken).ConfigureAwait(false);
            WritePlan(environment.Output, plan, options);
            if (!yes)
            {
                environment.Output.WriteLine("Run the command again with --yes to import the list.");
                return ExitCodes.Done;
            }

            IDownloadService downloads = services.GetRequiredService<IDownloadService>();
            if (options.Download && downloads.IsQueueBusyElsewhere)
            {
                await environment.Error.WriteLineAsync(
                        "Another ModShelf process is running the downloads of this library, such as the desktop app or a download "
                        + "command. Import the list there (the desktop app's Library › Import list…), or run this again once it has finished.")
                    .ConfigureAwait(false);
                return ExitCodes.Failed;
            }

            // Watch first, so no download that ends while the list is imported is missed.
            using CancellationTokenSource watching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IAsyncEnumerable<DownloadJobSnapshot> updates = downloads.WatchAsync(watching.Token);
            ListImportResult result = await lists.ImportAsync(plan, options, null, null, cancellationToken).ConfigureAwait(false);
            WriteResult(environment.Output, result);
            if (result.Queued.Count > 0)
            {
                await WaitForDownloadsAsync(environment.Output, downloads, updates, result.Queued, cancellationToken).ConfigureAwait(false);
                await watching.CancelAsync().ConfigureAwait(false);
            }

            return result.Problems.Count == 0 ? ExitCodes.Done : ExitCodes.Failed;
        }
        catch (InvalidDataException exception)
        {
            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.BadInput;
        }
        catch (ArgumentException exception)
        {
            await environment.Error.WriteLineAsync(exception.MessageForUser()).ConfigureAwait(false);
            return ExitCodes.BadInput;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            environment.Output.WriteLine("Stopped. What was queued stays in the queue, and downloads the next time ModShelf runs its downloads.");
            return ExitCodes.Failed;
        }
        finally
        {
            // Stopping the scheduler puts a running download back in the queue, keeping its partial file.
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the collection the link is a page of, saying what went wrong when it can't be.
    /// </summary>
    /// <returns>The collection as a list; or, once what went wrong is said, null and the exit code to end with.</returns>
    /// <exception cref="ArgumentException">The link is to a site's collections, but can't be read as one.</exception>
    private static async Task<(ModList? List, int ExitCode)> ReadCollectionAsync(
        CliEnvironment environment,
        IModLists lists,
        Uri link,
        CancellationToken cancellationToken)
    {
        (string Message, int ExitCode) problem;
        try
        {
            if (await lists.ReadCollectionAsync(link, cancellationToken).ConfigureAwait(false) is { } list)
            {
                return (list, ExitCodes.Done);
            }

            problem = (
                "That link isn't to a collection ModShelf can read. library import takes a list file library export wrote, "
                + "or the link of a collection's page, such as a Nexus Mods collection's.",
                ExitCodes.BadInput);
        }
        catch (AuthenticationFailedException)
        {
            problem = (
                "The site needs a linked account to read the collection, or refused the one linked. Link it with account link, "
                + "then run the same command again.",
                ExitCodes.Failed);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            problem = ("The site has no such collection.", ExitCodes.BadInput);
        }
        catch (HttpRequestException exception)
        {
            problem = ($"The site couldn't read the collection: {exception.Message}", ExitCodes.Failed);
        }

        await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
        return (null, problem.ExitCode);
    }

    /// <summary>
    /// Waits until none of the files queued is downloading or waiting its turn, saying how each ended: downloaded,
    /// failed, or waiting for the user to start it on its site's page.
    /// </summary>
    private static async Task WaitForDownloadsAsync(
        TextWriter output,
        IDownloadService downloads,
        IAsyncEnumerable<DownloadJobSnapshot> updates,
        IReadOnlyList<ModFileRef> queued,
        CancellationToken cancellationToken)
    {
        HashSet<ModFileRef> files = [.. queued];
        Dictionary<ModFileRef, DownloadJobSnapshot> latest = [];
        foreach (DownloadJobSnapshot job in await downloads.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (files.Contains(job.File))
            {
                latest[job.File] = job;
            }
        }

        HashSet<ModFileRef> underWay = [.. latest.Values.Where(job => IsUnderWay(job.Status)).Select(job => job.File)];
        if (underWay.Count > 0)
        {
            output.WriteLine($"Downloading {Count(underWay.Count, "file", "files")}. Ctrl+C stops; what's left downloads the next time ModShelf runs its downloads.");
            await foreach (DownloadJobSnapshot job in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (!underWay.Contains(job.File))
                {
                    continue;
                }

                latest[job.File] = job;
                if (!IsUnderWay(job.Status))
                {
                    underWay.Remove(job.File);
                    output.WriteLine(Ended(job));
                    if (underWay.Count == 0)
                    {
                        break;
                    }
                }
            }
        }

        DownloadJobSnapshot[] waiting = [.. latest.Values.Where(job => job.Status == DownloadJobStatus.AwaitingHandoff)];
        if (waiting.Length > 0)
        {
            output.WriteLine($"{Count(waiting.Length, "file waits", "files wait")} for you to start on its site, which hands it to ModShelf:");
            foreach (DownloadJobSnapshot job in waiting)
            {
                output.WriteLine($"  {job.FileName}: {job.HandoffPage?.AbsoluteUri ?? "its page on its site"}");
            }

            output.WriteLine("The desktop app's Downloads screen opens them one after another.");
        }
    }

    private static bool IsUnderWay(DownloadJobStatus status)
        => status is DownloadJobStatus.Queued or DownloadJobStatus.Resolving or DownloadJobStatus.Downloading or DownloadJobStatus.Verifying;

    private static string Ended(DownloadJobSnapshot job) => job.Status switch
    {
        DownloadJobStatus.Completed => $"Downloaded {job.FileName}.",
        DownloadJobStatus.Failed => $"Couldn't download {job.FileName} ({job.FailureReason}).",
        DownloadJobStatus.AwaitingHandoff => $"{job.FileName} waits for you to start it on its site.",
        DownloadJobStatus.Paused => $"{job.FileName} is paused.",
        _ => $"{job.FileName} was cancelled.",
    };

    private static void WritePlan(TextWriter output, ListImportPlan plan, ListImportOptions options)
    {
        string name = plan.List.Title is { } title ? $"The list \"{title}\"" : "The list";
        List<string> parts = [$"{plan.NewMods:N0} new to the library", $"{plan.ModsInLibrary:N0} in it already"];
        if (plan.ModsWithoutSite > 0)
        {
            string[] sites = [.. plan.Mods.Where(mod => mod.State == PlannedModState.NoSite).Select(mod => mod.Listed.Mod.Game.Source.Value).Distinct()];
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{plan.ModsWithoutSite:N0} of sites ModShelf can't reach here ({string.Join(", ", sites)})"));
        }

        output.WriteLine(string.Create(CultureInfo.CurrentCulture, $"{name} has {Count(plan.Mods.Count, "mod", "mods")}: {Joined(parts)}."));
        if (plan.List.FilesUnavailable > 0)
        {
            output.WriteLine($"{Count(plan.List.FilesUnavailable, "file it lists is", "files it lists are")} no longer offered by its site, so can't be downloaded.");
        }

        if (plan.List.Elsewhere.Count > 0)
        {
            output.WriteLine("It needs these from elsewhere too, which ModShelf doesn't download:");
            foreach (ListedElsewhere thing in plan.List.Elsewhere)
            {
                output.WriteLine($"  {thing.Name}{(thing.Optional ? " (optional)" : string.Empty)}: {thing.PageUrl?.AbsoluteUri ?? "no link given"}");
            }
        }
        if (!options.Download)
        {
            output.WriteLine("Importing would watch the mods new to the library, and download nothing.");
            return;
        }

        int files = plan.FilesToDownload(options.IncludeOptional);
        string queue = files == 0 ? "queue no files, as the library has them all" : $"queue {Count(files, "file", "files")} for the library";
        output.WriteLine($"Importing would watch the new mods the list watches, and {queue}.");
        if (!options.IncludeOptional && plan.OptionalFiles > 0)
        {
            output.WriteLine($"{Count(plan.OptionalFiles, "optional file is", "optional files are")} left out; --optional adds them.");
        }
    }

    private static void WriteResult(TextWriter output, ListImportResult result)
    {
        List<string> done = [];
        if (result.ModsAdded > 0)
        {
            done.Add($"Watched {Count(result.ModsAdded, "new mod", "new mods")}.");
        }

        if (result.FilesQueued > 0)
        {
            done.Add($"Queued {Count(result.FilesQueued, "file", "files")} for the library.");
        }

        if (result.FilesInLibrary > 0)
        {
            done.Add($"{Count(result.FilesInLibrary, "file was", "files were")} in the library already.");
        }

        output.WriteLine(done.Count == 0 ? "Nothing was imported." : string.Join(' ', done));
        if (result.Problems.Count == 0)
        {
            return;
        }

        output.WriteLine("Not done:");
        foreach (ListImportProblem problem in result.Problems)
        {
            output.WriteLine($"  {problem.ModName} ({problem.Mod.Game} {problem.Mod.Id}): {Said(problem)}");
        }

        if (result.Problems.Any(problem => problem.Kind == ListImportProblemKind.FileGone))
        {
            output.WriteLine("--newest-for-missing downloads a mod's newest main file in place of one its site no longer offers.");
        }
    }

    private static string Said(ListImportProblem problem) => problem.Kind switch
    {
        ListImportProblemKind.NoSite => $"ModShelf can't reach {problem.Mod.Game.Source.Value} here, so it's left out.",
        ListImportProblemKind.SiteFailed => $"its site couldn't be asked about it. {problem.Detail}".TrimEnd(),
        ListImportProblemKind.FileGone => $"its site no longer offers file {problem.File?.Id}.",
        _ => problem.File is { } file ? $"file {file.Id} couldn't be queued. {problem.Detail}".TrimEnd() : $"it couldn't be watched. {problem.Detail}".TrimEnd(),
    };

    private static string Count(int count, string one, string many)
        => string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? one : many)}");

    private static string Joined(List<string> parts)
        => parts.Count < 2 ? string.Concat(parts) : $"{string.Join(", ", parts[..^1])} and {parts[^1]}";
}
