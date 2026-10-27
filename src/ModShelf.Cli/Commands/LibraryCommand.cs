using System.CommandLine;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Library;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>library list</c>, <c>library watch &lt;link&gt;</c> and <c>library unwatch &lt;link&gt;</c>: the mods ModShelf
/// watches for updates or has downloaded files of.
/// </summary>
internal static class LibraryCommand
{
    public static Command Create(CliEnvironment environment)
    {
        Command list = new("list", "Shows the mods in the library: the ones watched for updates, and the ones with downloaded files.");
        list.SetAction((_, cancellationToken) => ListAsync(environment, cancellationToken));

        return new Command("library", "Shows the library of watched and downloaded mods, and watches mods for updates.")
        {
            list,
            CreateWatch(environment, watch: true),
            CreateWatch(environment, watch: false),
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

    private static async Task<int> ListAsync(CliEnvironment environment, CancellationToken cancellationToken)
    {
        // Without background downloads, starting the host only creates or migrates the database.
        using IHost host = environment.BuildHost(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<LibraryMod> mods = await host.Services.GetRequiredService<IModLibrary>()
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
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<int> WatchAsync(CliEnvironment environment, string linkText, bool watch, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(linkText, UriKind.Absolute, out Uri? link))
        {
            await environment.Error.WriteLineAsync("That isn't a complete link, such as https://www.nexusmods.com/stardewvalley/mods/2400.")
                .ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        using IHost host = environment.BuildHost(false);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            IModLibrary library = host.Services.GetRequiredService<IModLibrary>();
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await environment.Error.WriteLineAsync("Stopped.").ConfigureAwait(false);
            return ExitCodes.Interrupted;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
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
