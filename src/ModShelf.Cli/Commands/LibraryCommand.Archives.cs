using System.CommandLine;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Archives;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Cli.Common;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>library import-archives &lt;folder&gt; --game &lt;game&gt; [--yes]</c>: another mod manager's downloads, identified
/// and copied into the library (ADR 0101). The folder is only read.
/// </summary>
internal static partial class LibraryCommand
{
    private static Command CreateImportArchives(CliEnvironment environment)
    {
        Argument<string> folder = new("folder")
        {
            Description = "The folder of archives, such as Mod Organizer 2's or Vortex's downloads of the game. Its subfolders aren't read.",
        };
        Option<string> game = new("--game")
        {
            Description = "The game the archives are of, by its site and its key there, such as nexus/skyrimspecialedition; its key alone does while only one site's game has it.",
            Required = true,
        };
        Option<bool> yes = new("--yes")
        {
            Description = "Import the archives. Without it, the command only says what importing would do.",
        };
        Command command = new("import-archives", "Identifies the archives in a folder as files of a game's mods, and copies those the library lacks into it, watching their mods. Only copies: nothing in the folder changes, and nothing is installed. Says what it would do unless --yes is given. Ctrl+C stops it; run it again to go on.")
        {
            folder,
            game,
            yes,
        };
        command.SetAction((parsed, cancellationToken)
            => ImportArchivesAsync(environment, parsed.GetValue(folder)!, parsed.GetValue(game)!, parsed.GetValue(yes), cancellationToken));
        return command;
    }

    private static async Task<int> ImportArchivesAsync(CliEnvironment environment, string folder, string gameName, bool yes, CancellationToken cancellationToken)
    {
        string full;
        try
        {
            full = Path.GetFullPath(folder);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        if (!Directory.Exists(full))
        {
            await environment.Error.WriteLineAsync($"There's no folder at {full}.").ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        try
        {
            return await CommandHost.RunAsync(
                    environment,
                    async services =>
                    {
                        ISourceRegistry sources = services.GetRequiredService<ISourceRegistry>();
                        GameRef game = await SettingChanges.GameNamedAsync(
                                gameName,
                                services.GetRequiredService<ISettingsService>(),
                                services.GetRequiredService<IModLibrary>(),
                                sources,
                                cancellationToken)
                            .ConfigureAwait(false);
                        IArchiveImport archives = services.GetRequiredService<IArchiveImport>();
                        ArchiveScan scan = await archives.ScanAsync(full, game, cancellationToken).ConfigureAwait(false);
                        string site = CliText.SiteName(sources, game.Source);
                        if (scan.Archives.Count == 0)
                        {
                            environment.Output.WriteLine($"There are no archives (.7z, .zip or .rar) in {scan.Folder}.");
                            return ExitCodes.Done;
                        }

                        environment.Output.WriteLine(string.Create(
                            CultureInfo.CurrentCulture,
                            $"Identifying {Count(scan.Archives.Count, "archive", "archives")} of {game} in {scan.Folder} takes about {Count(scan.Requests, "request", "requests")} to {site}."));
                        ArchivePlan plan = await archives.IdentifyAsync(scan, null, cancellationToken).ConfigureAwait(false);
                        WriteArchivePlan(environment.Output, plan, site);
                        if (!yes)
                        {
                            if (plan.ToImport.Count > 0)
                            {
                                environment.Output.WriteLine("Run the command again with --yes to import them. The archives are only copied: nothing in the folder changes.");
                            }

                            return ExitCodes.Done;
                        }

                        if (plan.ToImport.Count == 0)
                        {
                            return ExitCodes.Done;
                        }

                        try
                        {
                            ArchiveImportResult result = await archives.ImportAsync(plan, null, cancellationToken).ConfigureAwait(false);
                            WriteArchiveResult(environment.Output, result);
                            return result.Problems.Count == 0 ? ExitCodes.Done : ExitCodes.Failed;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            environment.Output.WriteLine("Stopped. What was copied stays in the library; run the same command again to go on.");
                            return ExitCodes.Failed;
                        }
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

    private static void WriteArchivePlan(TextWriter output, ArchivePlan plan, string site)
    {
        foreach (PlannedArchive archive in plan.Archives)
        {
            output.WriteLine($"  {archive.Name}: {Described(archive, site)}");
        }

        List<string> parts = [];
        if (plan.ToImport.Count > 0)
        {
            parts.Add($"copy {Count(plan.ToImport.Count, "archive", "archives")} into the library" + (plan.NewMods > 0 ? $", watching {Count(plan.NewMods, "mod", "mods")} new to it" : string.Empty));
        }

        List<string> left = [];
        AddCount(left, plan.Count(ArchiveState.InLibrary), "is in the library already", "are in the library already");
        AddCount(left, plan.Count(ArchiveState.Duplicate), "is a copy of another here", "are copies of others here");
        AddCount(left, plan.Count(ArchiveState.Unknown), "is unknown", "are unknown");
        string leftAlone = left.Count == 0 ? string.Empty : $"{Joined(left)}, left where {(plan.Archives.Count - plan.ToImport.Count == 1 ? "it is" : "they are")}";
        output.WriteLine(parts.Count == 0
            ? $"Importing would copy nothing: {leftAlone}."
            : $"Importing would {parts[0]}{(leftAlone.Length == 0 ? "." : $"; {leftAlone}.")}");
    }

    private static string Described(PlannedArchive archive, string site) => archive.State switch
    {
        ArchiveState.Identified => $"{archive.ModName}, {archive.FileName} (file {archive.File!.Id} of mod {archive.File.Mod.Id}), {By(archive.By)}",
        ArchiveState.InLibrary => archive.ModName is null ? "in the library already" : $"in the library already, as {archive.ModName}, {archive.FileName}",
        ArchiveState.Duplicate => $"left alone: {archive.Why}",
        _ => $"unknown, left alone: {archive.Why ?? $"{site} couldn't tell which file it is."}",
    };

    private static string By(ArchiveClue? clue) => clue switch
    {
        ArchiveClue.Notes => "by its Mod Organizer note",
        ArchiveClue.Name => "by its name",
        _ => "by its content",
    };

    private static void WriteArchiveResult(TextWriter output, ArchiveImportResult result)
    {
        List<string> done = [$"Copied {Count(result.Imported.Count, "archive", "archives")} into the library"];
        if (result.ModsAdded > 0)
        {
            done[0] += $", and watched {Count(result.ModsAdded, "new mod", "new mods")}";
        }

        done[0] += ".";
        if (result.InLibrary > 0)
        {
            done.Add($"{Count(result.InLibrary, "was", "were")} in it already.");
        }

        output.WriteLine(string.Join(' ', done));
        if (result.Problems.Count == 0)
        {
            return;
        }

        output.WriteLine("Not copied:");
        foreach (ArchiveProblem problem in result.Problems)
        {
            output.WriteLine($"  {problem.Name}: {problem.Message}");
        }
    }

    private static void AddCount(List<string> parts, int count, string one, string many)
    {
        if (count > 0)
        {
            parts.Add($"{count:N0} {(count == 1 ? one : many)}");
        }
    }
}
