using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>library pin &lt;link&gt;</c>, <c>library unpin &lt;link&gt;</c>, <c>library skip &lt;link&gt; [--undo]</c> and
/// <c>library note &lt;link&gt; [text] [--clear]</c>: what the user keeps of a library mod, without asking its site.
/// </summary>
internal static partial class LibraryCommand
{
    private static Command CreatePin(CliEnvironment environment, bool pin)
    {
        Argument<string> link = new("link")
        {
            Description = "A link to the mod or one of its files, such as a mod's page on Nexus Mods.",
        };

        Command command = pin
            ? new("pin", "Pins a library mod at the version it has: its updates are still found, but aren't listed, counted or downloaded by Update all.") { link }
            : new("unpin", "Unpins a library mod, so its updates are offered again.") { link };
        command.SetAction((parsed, cancellationToken) => RunAsync(
            environment,
            parsed.GetValue(link)!,
            async (library, uri) => Pinned(await library.SetPinnedAsync(uri, pin, cancellationToken).ConfigureAwait(false), pin),
            cancellationToken));
        return command;
    }

    private static Command CreateSkip(CliEnvironment environment)
    {
        Argument<string> link = new("link")
        {
            Description = "The update's link, as library updates lists it.",
        };
        Option<bool> undo = new("--undo")
        {
            Description = "Offer the update again.",
        };

        Command command = new("skip", "Skips an update until a newer one comes, so it isn't listed, counted or downloaded by Update all.")
        {
            link,
            undo,
        };
        command.SetAction((parsed, cancellationToken) =>
        {
            bool skip = !parsed.GetValue(undo);
            return RunAsync(
                environment,
                parsed.GetValue(link)!,
                async (library, uri) =>
                {
                    LibraryChange change = await library.SkipUpdateAsync(uri, skip, cancellationToken).ConfigureAwait(false);
                    return Skipped(change, skip);
                },
                cancellationToken);
        });
        return command;
    }

    private static Command CreateNote(CliEnvironment environment)
    {
        Argument<string> link = new("link")
        {
            Description = "A link to the mod or one of its files, such as a mod's page on Nexus Mods.",
        };
        Argument<string?> text = new("text")
        {
            Description = $"The note, up to {LibraryMod.MaxNoteLength:N0} characters, in place of the one the mod has.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        Option<bool> clear = new("--clear")
        {
            Description = "Remove the mod's note.",
        };

        Command command = new("note", "Keeps a note of a library mod, such as why it's pinned. library list shows the notes.")
        {
            link,
            text,
            clear,
        };
        command.SetAction(async (parsed, cancellationToken) =>
        {
            string? note = parsed.GetValue(text);
            bool clearing = parsed.GetValue(clear);
            if (clearing == !string.IsNullOrWhiteSpace(note))
            {
                await environment.Error.WriteLineAsync(clearing
                        ? "Give a note or --clear, not both."
                        : "Give the note, or --clear to remove the one the mod has.")
                    .ConfigureAwait(false);
                return ExitCodes.BadInput;
            }

            return await RunAsync(
                    environment,
                    parsed.GetValue(link)!,
                    async (library, uri) => Noted(await library.SetNoteAsync(uri, note, cancellationToken).ConfigureAwait(false)),
                    cancellationToken)
                .ConfigureAwait(false);
        });
        return command;
    }

    /// <summary>
    /// Reads the link, makes the change, and says what it did; or why it couldn't.
    /// </summary>
    private static async Task<int> RunAsync(
        CliEnvironment environment, string linkText, Func<IModLibrary, Uri, Task<string>> change, CancellationToken cancellationToken)
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
                        string said = await change(services.GetRequiredService<IModLibrary>(), link).ConfigureAwait(false);
                        await environment.Output.WriteLineAsync(said).ConfigureAwait(false);
                        return ExitCodes.Done;
                    }
                    catch (KeyNotFoundException exception)
                    {
                        await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                        return ExitCodes.Failed;
                    }
                    catch (Exception exception) when (LinkProblems.Describe(exception) is { } problem)
                    {
                        await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                        return problem.ExitCode;
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Pinned(LibraryChange change, bool pin)
    {
        LibraryMod mod = change.After!;
        string version = VersionOf(mod) is { } label ? $" at {label}" : string.Empty;
        return (pin, change.Before!.IsPinned) switch
        {
            (true, true) => $"{mod.Name} is pinned already{version}.",
            (true, false) => $"Pinned {mod.Name}{version}: its updates are still found, but aren't offered. To unpin it: library unpin \"<its link>\"",
            (false, false) => $"{mod.Name} isn't pinned.",
            _ => $"Unpinned {mod.Name}: its updates are offered again.",
        };
    }

    private static string Skipped(LibraryChange change, bool skip)
    {
        LibraryMod before = change.Before!;
        LibraryMod mod = change.After!;
        ModFileRef? changed = skip
            ? mod.SkippedUpdates.FirstOrDefault(file => !before.SkippedUpdates.Contains(file))
            : before.SkippedUpdates.FirstOrDefault(file => !mod.SkippedUpdates.Contains(file));
        if (changed is null)
        {
            return skip ? $"That update of {mod.Name} is skipped already." : $"That update of {mod.Name} isn't skipped.";
        }

        LibraryUpdate update = mod.Updates.First(found => found.File == changed);
        string name = update.VersionLabel is { } version ? $"{update.Name} ({version})" : update.Name;
        return skip
            ? $"Skipped {name} of {mod.Name} until a newer update comes. To offer it again: library skip \"<its link>\" --undo"
            : $"{name} of {mod.Name} is offered again.";
    }

    private static string Noted(LibraryChange change) => (change.Before!.Note, change.After!.Note) switch
    {
        (null, null) => $"{change.After.Name} had no note.",
        (_, null) => $"Removed the note of {change.After.Name}.",
        _ => $"Kept the note of {change.After.Name}.",
    };

    /// <summary>
    /// The version the mod has: its newest main file's, or its newest file's; null when none says one.
    /// </summary>
    private static string? VersionOf(LibraryMod mod)
        => mod.Files
            .Where(file => file.VersionLabel is not null)
            .OrderByDescending(file => file.Category == ModFileCategory.Main)
            .ThenByDescending(file => file.UploadedAt)
            .FirstOrDefault()?.VersionLabel;
}
