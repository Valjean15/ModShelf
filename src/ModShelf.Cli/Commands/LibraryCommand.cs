using System.CommandLine;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>library list</c>, <c>library watch &lt;link&gt;</c>, <c>library unwatch &lt;link&gt;</c>,
/// <c>library forget &lt;link&gt;</c> and <c>library move &lt;folder&gt;</c>: the mods ModShelf watches for updates or
/// has downloaded files of, and where their files are. Checking for updates, rescanning, moving a game's files, and
/// pins, skipped updates and notes are in files of their own.
/// </summary>
internal static partial class LibraryCommand
{
    public static Command Create(CliEnvironment environment)
    {
        Option<bool> files = new("--files")
        {
            Description = "Also list each mod's files: version, size, SHA-256 and where each is.",
        };
        Command list = new("list", "Shows the mods in the library: the ones watched for updates, and the ones with downloaded files.")
        {
            files,
        };
        list.SetAction((parsed, cancellationToken) => ListAsync(environment, parsed.GetValue(files), cancellationToken));

        Argument<DirectoryInfo> folder = new("folder")
        {
            Description = "The library's new root folder. The library's files go to the same places under it.",
        };
        Command move = new("move", "Moves the library's files to another folder, which becomes the library's root. Ctrl+C stops it; run the same command again to finish.")
        {
            folder,
        };
        move.SetAction((parsed, cancellationToken) => MoveAsync(environment, parsed.GetValue(folder)!, cancellationToken));

        return new Command("library", "Shows the library of watched and downloaded mods, watches mods and checks them for updates, pins mods, skips updates, keeps notes, forgets and rescans mods, and moves the library or a game's files.")
        {
            list,
            CreateWatch(environment, watch: true),
            CreateWatch(environment, watch: false),
            CreatePin(environment, pin: true),
            CreatePin(environment, pin: false),
            CreateSkip(environment),
            CreateNote(environment),
            CreateForget(environment),
            CreateRescan(environment),
            CreateCheck(environment),
            CreateUpdates(environment),
            move,
            CreateMoveGame(environment),
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

    private static Command CreateForget(CliEnvironment environment)
    {
        Argument<string> link = new("link")
        {
            Description = "A link to the mod or one of its files, such as a mod's page on Nexus Mods.",
        };

        Command command = new("forget", "Takes a mod out of the library, with its updates, so it isn't watched any more. Its downloaded files stay on disk, no longer the library's.")
        {
            link,
        };
        command.SetAction((parsed, cancellationToken) => ForgetAsync(environment, parsed.GetValue(link)!, cancellationToken));
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

    private static async Task<int> ListAsync(CliEnvironment environment, bool files, CancellationToken cancellationToken)
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
                                (mod.IsWatched, mod.IsPinned) switch
                                {
                                    (true, true) => "yes, pinned",
                                    (false, true) => "no, pinned",
                                    (true, false) => "yes",
                                    _ => "no",
                                },
                                mod.Files.Count.ToString(CultureInfo.CurrentCulture),
                                mod.PageUrl?.AbsoluteUri ?? "-",
                            }),
                    ];
                    TextTable.Write(environment.Output, rows);
                    WriteNotes(environment.Output, mods);
                    if (files)
                    {
                        LibraryRoots roots;
                        try
                        {
                            roots = await services.GetRequiredService<ISettingsService>().GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (InvalidDataException exception)
                        {
                            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                            return ExitCodes.Failed;
                        }

                        WriteFiles(environment.Output, mods, roots);
                    }

                    return ExitCodes.Done;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The mods' notes, by mod as the mods are listed; nothing when no mod has one.
    /// </summary>
    private static void WriteNotes(TextWriter output, IReadOnlyList<LibraryMod> mods)
    {
        LibraryMod[] noted =
        [
            .. mods
                .Where(mod => mod.Note is not null)
                .OrderBy(mod => mod.Mod.Game.Source.Value, StringComparer.Ordinal)
                .ThenBy(mod => mod.Mod.Game.Key, StringComparer.Ordinal)
                .ThenBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
        if (noted.Length == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine("Notes:");
        foreach (LibraryMod mod in noted)
        {
            output.WriteLine($"{mod.Name} ({mod.Mod.Game.Key} {mod.Mod.Id}): {mod.Note!.ReplaceLineEndings(" / ")}");
        }
    }

    /// <summary>
    /// A table of the library's files, by mod as the mods are listed, the latest downloaded first. A file recorded before
    /// ModShelf kept hashes has none until a rescan reads it.
    /// </summary>
    private static void WriteFiles(TextWriter output, IReadOnlyList<LibraryMod> mods, LibraryRoots roots)
    {
        LibraryFile[] all =
        [
            .. mods
                .OrderBy(mod => mod.Mod.Game.Source.Value, StringComparer.Ordinal)
                .ThenBy(mod => mod.Mod.Game.Key, StringComparer.Ordinal)
                .ThenBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
                .SelectMany(mod => mod.Files.OrderByDescending(file => file.DownloadedAt)),
        ];
        output.WriteLine();
        if (all.Length == 0)
        {
            output.WriteLine("The library has no downloaded files.");
            return;
        }

        output.WriteLine(all.Length == 1
            ? "The library has 1 file."
            : string.Create(CultureInfo.CurrentCulture, $"The library has {all.Length:N0} files."));
        string[][] rows =
        [
            ["Game", "ID", "File", "Version", "Size", "SHA-256", "Path"],
            .. all.Select(file => new[]
            {
                file.File.Mod.Game.Key,
                file.File.Mod.Id,
                file.FileName,
                file.VersionLabel ?? "-",
                Sizes.Format(file.SizeInBytes),
                file.Sha256 ?? "-",
                roots.Resolve(file),
            }),
        ];
        TextTable.Write(output, rows);
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

    private static async Task<int> ForgetAsync(CliEnvironment environment, string linkText, CancellationToken cancellationToken)
    {
        if (!ModLinkArgument.TryRead(environment, linkText, out Uri? link))
        {
            return ExitCodes.BadInput;
        }

        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    try
                    {
                        LibraryChange change = await services.GetRequiredService<IModLibrary>().ForgetAsync(link, cancellationToken).ConfigureAwait(false);
                        await environment.Output.WriteLineAsync(Forgot(change.Before!)).ConfigureAwait(false);
                    }
                    catch (KeyNotFoundException exception)
                    {
                        // Nothing to forget, as unwatching a mod that isn't watched.
                        await environment.Output.WriteLineAsync(exception.Message).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
                    {
                        await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                        return ExitCodes.Failed;
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

    private static string Forgot(LibraryMod forgotten) => forgotten.Files.Count switch
    {
        0 => $"{forgotten.Name} is out of the library. It had no downloaded files.",
        1 => $"{forgotten.Name} is out of the library. Its downloaded file stays on disk.",
        _ => string.Create(CultureInfo.CurrentCulture, $"{forgotten.Name} is out of the library. Its {forgotten.Files.Count:N0} downloaded files stay on disk."),
    };

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
