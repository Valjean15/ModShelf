using System.CommandLine;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>library list</c>, <c>library watch &lt;link&gt;</c>, <c>library unwatch &lt;link&gt;</c> and
/// <c>library move &lt;folder&gt;</c>: the mods ModShelf watches for updates or has downloaded files of, and where their
/// files are. Checking for updates is in its own file.
/// </summary>
internal static partial class LibraryCommand
{
    public static Command Create(CliEnvironment environment)
    {
        Command list = new("list", "Shows the mods in the library: the ones watched for updates, and the ones with downloaded files.");
        list.SetAction((_, cancellationToken) => ListAsync(environment, cancellationToken));

        Argument<DirectoryInfo> folder = new("folder")
        {
            Description = "The library's new root folder. The library's files go to the same places under it.",
        };
        Command move = new("move", "Moves the library's files to another folder, which becomes the library's root. Ctrl+C stops it; run the same command again to finish.")
        {
            folder,
        };
        move.SetAction((parsed, cancellationToken) => MoveAsync(environment, parsed.GetValue(folder)!, cancellationToken));

        return new Command("library", "Shows the library of watched and downloaded mods, watches mods and checks them for updates, and moves the library.")
        {
            list,
            CreateWatch(environment, watch: true),
            CreateWatch(environment, watch: false),
            CreateCheck(environment),
            CreateUpdates(environment),
            move,
        };
    }

    private static Command CreateWatch(CliEnvironment environment, bool watch)
    {
        Argument<string> link = new("link")
        {
            Description = "A link to the mod or one of its files, such as a mod's page on Nexus Mods.",
        };

        Command command = watch
            ? new("watch", "Watches a mod for updates, adding it to the library. Nothing is downloaded.") { link }
            : new("unwatch", "Stops watching a mod for updates. A mod with downloaded files stays in the library.") { link };
        command.SetAction((parsed, cancellationToken) => WatchAsync(environment, parsed.GetValue(link)!, watch, cancellationToken));
        return command;
    }

    private static async Task<int> MoveAsync(CliEnvironment environment, DirectoryInfo folder, CancellationToken cancellationToken)
    {
        try
        {
            return await CommandHost.RunAsync(
                    environment,
                    async services =>
                    {
                        LibraryMove move = await services.GetRequiredService<ILibraryMover>()
                            .MoveAsync(folder.FullName, cancellationToken)
                            .ConfigureAwait(false);
                        if (string.Equals(move.From, move.To, StringComparison.Ordinal))
                        {
                            environment.Output.WriteLine($"The library is in {move.To} already.");
                            return ExitCodes.Done;
                        }

                        environment.Output.WriteLine(move.Moved == 1
                            ? $"Moved 1 file from {move.From} to {move.To}."
                            : string.Create(CultureInfo.CurrentCulture, $"Moved {move.Moved:N0} files from {move.From} to {move.To}."));
                        if (move.AlreadyThere > 0)
                        {
                            environment.Output.WriteLine(move.AlreadyThere == 1
                                ? "1 file was there already, from a move that stopped part-way."
                                : string.Create(CultureInfo.CurrentCulture, $"{move.AlreadyThere:N0} files were there already, from a move that stopped part-way."));
                        }

                        if (move.Missing > 0)
                        {
                            environment.Output.WriteLine(move.Missing == 1
                                ? "1 file wasn't on disk. The library still has it, to download again."
                                : string.Create(CultureInfo.CurrentCulture, $"{move.Missing:N0} files weren't on disk. The library still has them, to download again."));
                        }

                        environment.Output.WriteLine($"The library is in {move.To} now.");
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
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await environment.Error.WriteLineAsync(
                    "Stopped. The library stays where it was until every file is moved; run the same command again to finish.")
                .ConfigureAwait(false);
            return ExitCodes.Interrupted;
        }
    }

    private static async Task<int> ListAsync(CliEnvironment environment, CancellationToken cancellationToken)
    {
        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    IReadOnlyList<LibraryMod> mods = await services.GetRequiredService<IModLibrary>()
                        .ListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (mods.Count == 0)
                    {
                        environment.Output.WriteLine("The library is empty. To watch a mod for updates: library watch \"<its link>\"");
                        return ExitCodes.Done;
                    }

                    environment.Output.WriteLine(mods.Count == 1
                        ? "The library has 1 mod."
                        : string.Create(CultureInfo.CurrentCulture, $"The library has {mods.Count:N0} mods."));
                    string[][] rows =
                    [
                        ["Game", "ID", "Name", "Watched", "Files", "Link"],
                        .. mods
                            .OrderBy(mod => mod.Mod.Game.Source.Value, StringComparer.Ordinal)
                            .ThenBy(mod => mod.Mod.Game.Key, StringComparer.Ordinal)
                            .ThenBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
                            .Select(mod => new[]
                            {
                                mod.Mod.Game.Key,
                                mod.Mod.Id,
                                mod.Name,
                                mod.IsWatched ? "yes" : "no",
                                mod.Files.Count.ToString(CultureInfo.CurrentCulture),
                                mod.PageUrl?.AbsoluteUri ?? "-",
                            }),
                    ];
                    TextTable.Write(environment.Output, rows);
                    return ExitCodes.Done;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> WatchAsync(CliEnvironment environment, string linkText, bool watch, CancellationToken cancellationToken)
    {
        if (!ModLinkArgument.TryRead(environment, linkText, out Uri? link))
        {
            return ExitCodes.BadInput;
        }

        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    IModLibrary library = services.GetRequiredService<IModLibrary>();
                    try
                    {
                        if (watch)
                        {
                            LibraryMod watched = await library.WatchAsync(link, cancellationToken).ConfigureAwait(false);
                            await environment.Output.WriteLineAsync($"Watching {watched.Name} ({Describe(watched.Mod)}).").ConfigureAwait(false);
                        }
                        else
                        {
                            LibraryChange change = await library.UnwatchAsync(link, cancellationToken).ConfigureAwait(false);
                            await environment.Output.WriteLineAsync(Unwatched(change)).ConfigureAwait(false);
                        }
                    }
                    catch (Exception exception) when (LinkProblems.Describe(exception) is { } problem)
                    {
                        await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                        return problem.ExitCode;
                    }

                    return ExitCodes.Done;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Unwatched(LibraryChange change) => change switch
    {
        { Before: null } => $"Mod {change.Mod.Id} of {change.Mod.Game.Key} on {change.Mod.Game.Source} isn't in the library.",
        { Before.IsWatched: false } => $"{change.Before.Name} isn't watched.",
        { After: null } => $"Stopped watching {change.Before.Name}. It had no downloaded files, so it's out of the library.",
        { After.Files.Count: 1 } => $"Stopped watching {change.Before.Name}. Its downloaded file stays in the library.",
        _ => string.Create(
            CultureInfo.CurrentCulture,
            $"Stopped watching {change.Before.Name}. Its {change.After.Files.Count:N0} downloaded files stay in the library."),
    };

    private static string Describe(ModRef mod) => $"mod {mod.Id} of {mod.Game.Key} on {mod.Game.Source}";
}
