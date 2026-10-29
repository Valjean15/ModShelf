using System.CommandLine;
using System.Globalization;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Watchlist;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>mod tracked [--source]</c>, <c>mod track &lt;link&gt;</c> and <c>mod untrack &lt;link&gt;</c>: the mods the linked
/// account tracks on its site, such as Nexus Mods' tracked mods.
/// </summary>
internal static partial class ModCommand
{
    private const string LinkedAccountForTracking =
        "The site needs a linked account to track mods, or refused the one linked. Link it with account link, then run the "
        + "same command again.";

    private static Command CreateTracked(CliEnvironment environment)
    {
        Option<string?> source = new("--source")
        {
            Description = "The site, such as nexus. Needed only when more than one site can track mods.",
            HelpName = "source",
        };

        Command tracked = new("tracked", "Lists the mods the linked account tracks on the site.") { source };
        tracked.SetAction((parsed, cancellationToken) => TrackedAsync(environment, parsed.GetValue(source), cancellationToken));
        return tracked;
    }

    private static Command CreateTrack(CliEnvironment environment, bool track)
    {
        Argument<string> link = new("link")
        {
            Description = "A link to the mod or one of its files, such as a mod's page on Nexus Mods.",
        };

        Command command = track
            ? new("track", "Tracks the mod on its site with the linked account, as its Track button does. Tracking it again changes nothing.") { link }
            : new("untrack", "Stops tracking the mod on its site. Untracking a mod not tracked changes nothing.") { link };
        command.SetAction((parsed, cancellationToken) => TrackAsync(environment, parsed.GetValue(link)!, track, cancellationToken));
        return command;
    }

    private static async Task<int> TrackedAsync(CliEnvironment environment, string? sourceText, CancellationToken cancellationToken)
    {
        using IHost host = environment.BuildHost(false);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            IWatchlists watchlists = host.Services.GetRequiredService<IWatchlists>();
            if (ChooseSource(watchlists.Sources, sourceText) is not { } source)
            {
                await environment.Error.WriteLineAsync(SourceProblem(watchlists.Sources, sourceText, "track mods")).ConfigureAwait(false);
                return ExitCodes.BadInput;
            }

            IReadOnlyList<ModRef> tracked;
            try
            {
                tracked = await watchlists.GetWatchedAsync(source, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (ListingProblem(exception) is { } problem)
            {
                await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                return problem.ExitCode;
            }

            WriteTracked(environment.Output, tracked, source);
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

    private static async Task<int> TrackAsync(CliEnvironment environment, string linkText, bool track, CancellationToken cancellationToken)
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
            IWatchlists watchlists = host.Services.GetRequiredService<IWatchlists>();
            ModRef mod;
            try
            {
                mod = track
                    ? await watchlists.WatchAsync(link, cancellationToken).ConfigureAwait(false)
                    : await watchlists.UnwatchAsync(link, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (TrackingProblem(exception, track ? "track the mod" : "stop tracking the mod") is { } problem)
            {
                await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                return problem.ExitCode;
            }

            string which = $"mod {mod.Id} of {mod.Game.Key} on {mod.Game.Source}";
            await environment.Output.WriteLineAsync(track ? $"Tracking {which}." : $"No longer tracking {which}.").ConfigureAwait(false);
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

    /// <summary>
    /// What to say when the tracked mods can't be listed. The question isn't about a link, so the site having no such
    /// list says only that.
    /// </summary>
    private static (string Message, int ExitCode)? ListingProblem(Exception exception) => exception switch
    {
        AuthenticationFailedException => (LinkedAccountForTracking, ExitCodes.Failed),
        HttpRequestException or InvalidDataException => ($"The site couldn't list the tracked mods: {exception.Message}", ExitCodes.Failed),
        _ => null,
    };

    /// <param name="doing">What failed, such as "track the mod".</param>
    private static (string Message, int ExitCode)? TrackingProblem(Exception exception, string doing) => exception switch
    {
        AuthenticationFailedException => (LinkedAccountForTracking, ExitCodes.Failed),
        HttpRequestException { StatusCode: not (HttpStatusCode.NotFound or HttpStatusCode.Gone) } failed
            => ($"The site couldn't {doing}: {failed.Message}", ExitCodes.Failed),
        _ => LinkProblems.Describe(exception),
    };

    /// <summary>
    /// By game, then by ID, numbers in numeric order.
    /// </summary>
    private static void WriteTracked(TextWriter output, IReadOnlyList<ModRef> tracked, SourceId source)
    {
        if (tracked.Count == 0)
        {
            output.WriteLine($"The account tracks no mods on {source}.");
            return;
        }

        output.WriteLine(tracked.Count == 1 ? $"The account tracks 1 mod on {source}." : $"The account tracks {Count(tracked.Count)} mods on {source}.");
        string[][] rows =
        [
            ["Game", "ID"],
            .. tracked
                .OrderBy(mod => mod.Game.Key, StringComparer.Ordinal)
                .ThenBy(mod => long.TryParse(mod.Id, NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : long.MaxValue)
                .ThenBy(mod => mod.Id, StringComparer.Ordinal)
                .Select(mod => new[] { mod.Game.Key, mod.Id }),
        ];
        TextTable.Write(output, rows);
        output.WriteLine("To stop tracking one: mod untrack \"<its link>\"");
    }
}
