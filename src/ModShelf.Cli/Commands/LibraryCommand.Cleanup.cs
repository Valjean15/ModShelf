using System.CommandLine;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Cleanup;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Cli.Common;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>library cleanup [--game &lt;game&gt;] [--delete &lt;group&gt; [--yes]]</c>: what could go from the library's folders
/// to free up space, and one group of it deleted (ADR 0104).
/// </summary>
internal static partial class LibraryCommand
{
    /// <summary>
    /// The groups by the names <c>--delete</c> takes, in the order they're shown.
    /// </summary>
    private static readonly (string Name, CleanupGroup Group, string Heading)[] CleanupGroups =
    [
        ("archived", CleanupGroup.Archived, "Archived old versions"),
        ("duplicates", CleanupGroup.Duplicates, "Duplicates"),
        ("unwatched", CleanupGroup.Unwatched, "Files of mods not watched"),
        ("unmatched", CleanupGroup.Unmatched, "Files that match nothing"),
    ];

    private static Command CreateCleanup(CliEnvironment environment)
    {
        Option<string?> game = new("--game")
        {
            Description = "Only this game's files, by its site and its key there, such as nexus/stardewvalley; its key alone does while only one site's game has it.",
        };
        Option<string?> delete = new("--delete")
        {
            Description = "The group whose files that can go to delete: archived, duplicates, unwatched or unmatched. Without --yes, the command only says what deleting would do.",
        };
        delete.AcceptOnlyFromAmong([.. CleanupGroups.Select(group => group.Name)]);
        Option<bool> yes = new("--yes")
        {
            Description = "Delete the files of the group --delete names.",
        };
        Command command = new("cleanup", "Lists what could go from the library's folders to free up space: archived old versions, duplicates, files of mods not watched, and files that match nothing. A file that may be the only copy left, such as one its site no longer offers, is kept. Deletes one group's files with --delete and --yes; nothing is deleted otherwise.")
        {
            game,
            delete,
            yes,
        };
        command.SetAction((parsed, cancellationToken) => CleanupAsync(
            environment, parsed.GetValue(game), parsed.GetValue(delete), parsed.GetValue(yes), cancellationToken));
        return command;
    }

    private static async Task<int> CleanupAsync(CliEnvironment environment, string? gameName, string? deleteName, bool yes, CancellationToken cancellationToken)
    {
        if (yes && deleteName is null)
        {
            await environment.Error.WriteLineAsync("--yes deletes the files of the group --delete names: archived, duplicates, unwatched or unmatched.")
                .ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        try
        {
            return await CommandHost.RunAsync(
                    environment,
                    async services =>
                    {
                        ISourceRegistry sources = services.GetRequiredService<ISourceRegistry>();
                        GameRef? game = gameName is null
                            ? null
                            : await SettingChanges.GameNamedAsync(
                                    gameName, services.GetRequiredService<ISettingsService>(), services.GetRequiredService<IModLibrary>(), sources, cancellationToken)
                                .ConfigureAwait(false);
                        ILibraryCleanup cleanup = services.GetRequiredService<ILibraryCleanup>();
                        CleanupPlan plan = await cleanup.PlanAsync(game, null, cancellationToken).ConfigureAwait(false);
                        WriteCleanupPlan(environment.Output, plan, sources);
                        if (deleteName is null)
                        {
                            if (plan.CanFree > 0)
                            {
                                environment.Output.WriteLine("Run library cleanup --delete <group> --yes to delete a group's files that can go: archived, duplicates, unwatched or unmatched.");
                            }

                            return ExitCodes.Done;
                        }

                        (string name, CleanupGroup group, string heading) = CleanupGroups.Single(known => known.Name == deleteName);
                        IReadOnlyList<CleanupEntry> chosen = plan.CanGoIn(group);
                        if (chosen.Count == 0)
                        {
                            environment.Output.WriteLine($"No {heading.ToLowerInvariant()} can go.");
                            return ExitCodes.Done;
                        }

                        long size = chosen.Sum(entry => entry.SizeInBytes);
                        if (!yes)
                        {
                            environment.Output.WriteLine(
                                $"Run it again with --yes to delete the {Count(chosen.Count, "file", "files")} of {name} that can go, {Sizes.Format(size)}.");
                            return ExitCodes.Done;
                        }

                        try
                        {
                            CleanupResult result = await cleanup.DeleteAsync(chosen, null, cancellationToken).ConfigureAwait(false);
                            WriteCleanupResult(environment.Output, result);
                            return result.Failures.Count == 0 ? ExitCodes.Done : ExitCodes.Failed;
                        }
                        catch (InvalidOperationException exception)
                        {
                            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                            return ExitCodes.Failed;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            environment.Output.WriteLine("Stopped. The files deleted so far are gone; run the same command again to go on.");
                            return ExitCodes.Interrupted;
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
        catch (InvalidDataException exception)
        {
            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.Failed;
        }
    }

    /// <summary>
    /// The files that could go, as a table, then each group's count and size, and the total.
    /// </summary>
    private static void WriteCleanupPlan(TextWriter output, CleanupPlan plan, ISourceRegistry sources)
    {
        if (plan.Entries.Count == 0)
        {
            output.WriteLine(plan.Game is { } game
                ? $"Nothing to free up: {game}'s files hold no archived old versions, duplicates, files of mods not watched or files that match nothing."
                : "Nothing to free up: the library's folders hold no archived old versions, duplicates, files of mods not watched or files that match nothing.");
            return;
        }

        string[][] rows =
        [
            ["Group", "Game", "Mod", "Path", "Size", "State"],
            .. plan.Entries.Select(entry => new[]
            {
                CleanupGroups.Single(known => known.Group == entry.Group).Name,
                entry.Mod.Game.Key,
                entry.ModName,
                entry.RelativePath,
                Sizes.Format(entry.SizeInBytes),
                StateOf(entry, CliText.SiteName(sources, entry.Mod.Game.Source)),
            }),
        ];
        TextTable.Write(output, rows);

        foreach ((_, CleanupGroup group, string heading) in CleanupGroups)
        {
            CleanupEntry[] all = [.. plan.Entries.Where(entry => entry.Group == group)];
            if (all.Length == 0)
            {
                continue;
            }

            IReadOnlyList<CleanupEntry> canGo = plan.CanGoIn(group);
            int kept = all.Length - canGo.Count;
            string canGoText = canGo.Count == 0
                ? "none can go"
                : $"{Count(canGo.Count, "file", "files")} can go, {Sizes.Format(canGo.Sum(entry => entry.SizeInBytes))}";
            output.WriteLine(kept == 0 ? $"{heading}: {canGoText}." : $"{heading}: {canGoText}; {Count(kept, "file", "files")} kept.");
        }

        int total = plan.Entries.Count(entry => entry.CanGo);
        output.WriteLine(string.Create(
            CultureInfo.CurrentCulture,
            $"In all, {(total == 0 ? "nothing" : $"{Count(total, "file", "files")}, {Sizes.Format(plan.CanFree)},")} can go."));
    }

    private static string StateOf(CleanupEntry entry, string site) => entry.Kept switch
    {
        null when entry.SameAs is { } copy => $"can go: a copy of {copy.File.RelativePath}",
        null => "can go",
        KeptBecause.ModGone => $"kept: the mod is gone from {site}, so it may be the only copy",
        KeptBecause.NotOffered => $"kept: {site} no longer offers it",
        KeptBecause.MatchesSiteFile => $"kept: it's {entry.KeptDetail} on {site}; library rescan adds it",
        _ => $"kept: couldn't ask {site}: {entry.KeptDetail}",
    };

    private static void WriteCleanupResult(TextWriter output, CleanupResult result)
    {
        output.WriteLine($"Deleted {Count(result.Deleted.Count, "file", "files")}, freeing {Sizes.Format(result.Freed)}.");
        if (result.Failures.Count == 0)
        {
            return;
        }

        output.WriteLine("Not deleted:");
        foreach (CleanupFailure failure in result.Failures)
        {
            output.WriteLine($"  {failure.Entry.Path}: {failure.Message}");
        }
    }
}
