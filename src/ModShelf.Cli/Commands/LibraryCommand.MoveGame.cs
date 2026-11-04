using System.CommandLine;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Cli.Common;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>library move-game &lt;game&gt; [&lt;folder&gt;] [--yes]</c>: says what moving a game's files to a folder of its own,
/// or back into the library's, would do, then moves them when asked to.
/// </summary>
internal static partial class LibraryCommand
{
    private static Command CreateMoveGame(CliEnvironment environment)
    {
        Argument<string> game = new("game")
        {
            Description = "The game, by its site and its key there, such as nexus/stardewvalley; its key alone does while only one site's game has it.",
        };
        Argument<DirectoryInfo?> folder = new("folder")
        {
            Description = "The game's new folder; its files go to the same places under it. Leave it out to move them back into the library's folder.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        Option<bool> yes = new("--yes")
        {
            Description = "Move the files. Without it, the command only says what it would move.",
        };
        Command command = new(
            "move-game",
            "Moves a game's files to a folder of its own, which becomes the game's folder (games.<site>/<game>.root), or back into the library's. Says what it would move unless --yes is given. Ctrl+C stops it and puts every file back.")
        {
            game,
            folder,
            yes,
        };
        command.SetAction((parsed, cancellationToken)
            => MoveGameAsync(environment, parsed.GetValue(game)!, parsed.GetValue(folder)?.FullName, parsed.GetValue(yes), cancellationToken));
        return command;
    }

    private static async Task<int> MoveGameAsync(CliEnvironment environment, string written, string? folder, bool yes, CancellationToken cancellationToken)
    {
        try
        {
            return await CommandHost.RunAsync(
                    environment,
                    async services =>
                    {
                        GameRef game = await SettingChanges.GameNamedAsync(
                                written,
                                services.GetRequiredService<ISettingsService>(),
                                services.GetRequiredService<IModLibrary>(),
                                services.GetRequiredService<ISourceRegistry>(),
                                cancellationToken)
                            .ConfigureAwait(false);
                        ILibraryMover mover = services.GetRequiredService<ILibraryMover>();
                        GameMovePlan plan = await mover.PlanGameMoveAsync(game, folder, cancellationToken).ConfigureAwait(false);
                        if (string.Equals(plan.From, plan.To, StringComparison.Ordinal))
                        {
                            environment.Output.WriteLine($"{game}'s files are in {plan.To} already.");
                            return ExitCodes.Done;
                        }

                        WritePlan(environment.Output, plan);
                        if (!plan.Fits)
                        {
                            await environment.Error.WriteLineAsync(
                                    "There isn't room for them there: free some space on that drive, or choose another folder.")
                                .ConfigureAwait(false);
                            return ExitCodes.Failed;
                        }

                        if (!yes)
                        {
                            environment.Output.WriteLine("Nothing moved yet. To move them: add --yes.");
                            return ExitCodes.Done;
                        }

                        LibraryMove move = await mover.MoveGameAsync(game, folder, null, cancellationToken).ConfigureAwait(false);
                        environment.Output.WriteLine(move.Moved == 1
                            ? $"Moved 1 file from {move.From} to {move.To}."
                            : string.Create(CultureInfo.CurrentCulture, $"Moved {move.Moved:N0} files from {move.From} to {move.To}."));
                        environment.Output.WriteLine($"{game}'s files are in {move.To} now.");
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
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            // The mover puts back what it moved, and says so.
            await environment.Error.WriteLineAsync(exception.Message.StartsWith("The move was stopped.", StringComparison.Ordinal)
                    ? exception.Message
                    : "Stopped before any file moved.")
                .ConfigureAwait(false);
            return ExitCodes.Interrupted;
        }
    }

    /// <summary>
    /// What a game's move would do: how many files and how much they take, the drive and its room, and the files there
    /// already or missing.
    /// </summary>
    private static void WritePlan(TextWriter output, GameMovePlan plan)
    {
        string files = plan.Files == 1 ? "1 file" : string.Create(CultureInfo.CurrentCulture, $"{plan.Files:N0} files");
        output.WriteLine($"{plan.Game}'s files move from {plan.From} to {plan.To}: {files}, {Sizes.Format(plan.Bytes)}.");
        output.WriteLine(plan.SameDrive
            ? "Both folders are on the same drive, so the files move without being copied."
            : plan.FreeSpace is { } free
                ? $"They go to another drive, which has {Sizes.Format(free)} free."
                : "They go to another drive, whose free space this system can't tell.");
        if (plan.AlreadyThere > 0)
        {
            output.WriteLine(plan.AlreadyThere == 1
                ? "1 file is there already, at its size, so it counts as moved."
                : string.Create(CultureInfo.CurrentCulture, $"{plan.AlreadyThere:N0} files are there already, at their sizes, so they count as moved."));
        }

        if (plan.CopiesKept > 0)
        {
            output.WriteLine(plan.CopiesKept == 1
                ? $"Its copy in {plan.From} stays; delete it yourself once you're sure."
                : string.Create(CultureInfo.CurrentCulture, $"Of those, {plan.CopiesKept:N0} have copies in {plan.From}, which stay; delete them yourself once you're sure."));
        }

        if (plan.Missing > 0)
        {
            output.WriteLine(plan.Missing == 1
                ? "1 file isn't on disk. The library still has it, to download again."
                : string.Create(CultureInfo.CurrentCulture, $"{plan.Missing:N0} files aren't on disk. The library still has them, to download again."));
        }
    }
}
