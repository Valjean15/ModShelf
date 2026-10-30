using System.CommandLine;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Catalog;
using ModShelf.Application.Library;
using ModShelf.Application.Updates;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>library check</c> and <c>library updates [--changelog]</c>: checking the watched mods for updates, and the
/// updates found.
/// </summary>
internal static partial class LibraryCommand
{
    private static Command CreateCheck(CliEnvironment environment)
    {
        Command check = new("check", "Checks the watched mods for updates now, and shows the new ones.");
        check.SetAction((_, cancellationToken) => CheckAsync(environment, cancellationToken));
        return check;
    }

    private static Command CreateUpdates(CliEnvironment environment)
    {
        Option<bool> changelog = new("--changelog")
        {
            Description = "Also show each update's changes, from its mod's changelog. This asks each mod's site.",
        };
        Command updates = new("updates", "Shows the updates the checks found, with the link to download each.") { changelog };
        updates.SetAction((parsed, cancellationToken) => UpdatesAsync(environment, parsed.GetValue(changelog), cancellationToken));
        return updates;
    }

    private static async Task<int> CheckAsync(CliEnvironment environment, CancellationToken cancellationToken)
    {
        try
        {
            return await CommandHost.RunAsync(
                    environment,
                    async services =>
                    {
                        IReadOnlyList<LibraryMod> before = await services.GetRequiredService<IModLibrary>().ListAsync(cancellationToken).ConfigureAwait(false);
                        if (!before.Any(mod => mod.IsWatched))
                        {
                            environment.Output.WriteLine("No mods are watched. To watch one: library watch \"<its link>\"");
                            return ExitCodes.Done;
                        }

                        UpdateCheck check = await services.GetRequiredService<IUpdateChecker>().CheckAsync(cancellationToken).ConfigureAwait(false);
                        environment.Output.WriteLine(string.Create(
                            CultureInfo.CurrentCulture,
                            $"Checked {CliText.Count(check.Checked, "mod", "mods")} with {CliText.Count(check.Requests, "request", "requests")}."));
                        if (check.Found.Count == 0)
                        {
                            environment.Output.WriteLine("No new updates.");
                        }
                        else
                        {
                            IReadOnlyList<LibraryMod> library = await services.GetRequiredService<IModLibrary>().ListAsync(cancellationToken).ConfigureAwait(false);
                            environment.Output.WriteLine(check.Found.Count == 1 ? "1 new update:" : string.Create(CultureInfo.CurrentCulture, $"{check.Found.Count:N0} new updates:"));
                            WriteUpdates(environment.Output, library, check.Found);
                            environment.Output.WriteLine("To see every update: library updates");
                        }

                        foreach (UpdateCheckFailure failure in check.Failures)
                        {
                            string what = failure.Mod is { } mod ? $"mod {mod.Id} of {failure.Game.Key}" : failure.Game.Key;
                            await environment.Error.WriteLineAsync($"Couldn't check {what} on {failure.Game.Source}: {failure.Reason}").ConfigureAwait(false);
                        }

                        return check.Failures.Count == 0 ? ExitCodes.Done : ExitCodes.Failed;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await environment.Error.WriteLineAsync("Stopped. The mods checked so far keep what was found.").ConfigureAwait(false);
            return ExitCodes.Interrupted;
        }
    }

    private static async Task<int> UpdatesAsync(CliEnvironment environment, bool changelog, CancellationToken cancellationToken)
    {
        try
        {
            return await CommandHost.RunAsync(
                    environment,
                    async services =>
                    {
                        IReadOnlyList<LibraryMod> library = await services.GetRequiredService<IModLibrary>().ListAsync(cancellationToken).ConfigureAwait(false);
                        LibraryUpdate[] updates = [.. library.SelectMany(mod => mod.Updates)];
                        if (updates.Length == 0)
                        {
                            DateTimeOffset? checkedAt = library.Where(mod => mod.IsWatched).Min(mod => mod.CheckedAt);
                            environment.Output.WriteLine(checkedAt is { } at
                                ? $"There are no updates. The watched mods were checked {CliText.Time(at)} or later; to check now: library check"
                                : "There are no updates. To check the watched mods now: library check");
                            return ExitCodes.Done;
                        }

                        int mods = updates.Select(update => update.File.Mod).Distinct().Count();
                        environment.Output.WriteLine(string.Create(
                            CultureInfo.CurrentCulture,
                            $"{CliText.Count(updates.Length, "update", "updates")} for {CliText.Count(mods, "mod", "mods")}:"));
                        WriteUpdates(environment.Output, library, updates);
                        environment.Output.WriteLine("To download one: download \"<its link>\"");
                        if (changelog)
                        {
                            await WriteChangelogsAsync(environment, services.GetRequiredService<IModLookup>(), updates, cancellationToken).ConfigureAwait(false);
                        }

                        return ExitCodes.Done;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.Failed;
        }
    }

    /// <summary>
    /// A table of updates, by mod name and then the newest first.
    /// </summary>
    private static void WriteUpdates(TextWriter output, IReadOnlyList<LibraryMod> library, IReadOnlyList<LibraryUpdate> updates)
    {
        Dictionary<ModRef, LibraryMod> mods = library.ToDictionary(mod => mod.Mod);
        string[][] rows =
        [
            ["Mod", "Update", "Version", "Uploaded", "Replaces", "Link"],
            .. updates
                .OrderBy(update => mods.GetValueOrDefault(update.File.Mod)?.Name ?? update.File.Mod.Id, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(update => update.UploadedAt)
                .Select(update =>
                {
                    LibraryMod? mod = mods.GetValueOrDefault(update.File.Mod);
                    return new[]
                    {
                        mod?.Name ?? update.File.Mod.Id,
                        update.Name,
                        update.VersionLabel ?? "-",
                        CliText.Time(update.UploadedAt),
                        mod?.Files.FirstOrDefault(file => file.File == update.Replaces)?.FileName ?? "-",
                        update.PageUrl?.AbsoluteUri ?? "-",
                    };
                }),
        ];
        TextTable.Write(output, rows);
    }

    /// <summary>
    /// Each update's changes, from its mod's changelog: the entry for the update's version label, when there's one, as
    /// the site names it. Each mod's changelog is read once.
    /// </summary>
    private static async Task WriteChangelogsAsync(
        CliEnvironment environment,
        IModLookup lookup,
        IReadOnlyList<LibraryUpdate> updates,
        CancellationToken cancellationToken)
    {
        foreach (IGrouping<ModRef, LibraryUpdate> mod in updates.GroupBy(update => update.File.Mod))
        {
            List<(LibraryUpdate Update, IReadOnlyList<string> Changes)> entries = [];
            try
            {
                foreach (LibraryUpdate update in mod.OrderByDescending(update => update.UploadedAt))
                {
                    IReadOnlyList<string> changes = update.VersionLabel is { } version
                        ? await lookup.GetChangesAsync(mod.Key, version, cancellationToken).ConfigureAwait(false) ?? []
                        : [];
                    entries.Add((update, changes));
                }
            }
            catch (Exception exception) when (LinkProblems.Describe(exception) is { } problem)
            {
                await environment.Error.WriteLineAsync($"Couldn't read the changelog of mod {mod.Key.Id}: {problem.Message}").ConfigureAwait(false);
                continue;
            }

            foreach ((LibraryUpdate update, IReadOnlyList<string> changes) in entries)
            {
                environment.Output.WriteLine();
                environment.Output.WriteLine(update.VersionLabel is null ? $"{update.Name}:" : $"{update.Name} ({update.VersionLabel}):");
                if (changes.Count == 0)
                {
                    environment.Output.WriteLine("  The changelog says nothing about this version.");
                    continue;
                }

                foreach (string change in changes)
                {
                    environment.Output.WriteLine($"  - {change}");
                }
            }
        }
    }


}
