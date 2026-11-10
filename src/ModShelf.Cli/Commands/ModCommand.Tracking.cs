using System.CommandLine;
using System.Globalization;
using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Sources;
using ModShelf.Application.Watchlist;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>mod tracked [--source]</c>, <c>mod track &lt;link&gt;</c> and <c>mod untrack &lt;link&gt;</c>: the mods the linked
/// account follows on its site, such as Nexus Mods' tracked mods, said in the site's words.
/// </summary>
internal static partial class ModCommand
{
    private const string LinkAccountAgain = "Link it with account link, then run the same command again.";

    private static Command CreateTracked(CliEnvironment environment)
    {
        Option<string?> source = new("--source")
        {
            Description = "The site, such as nexus. Needed only when more than one site can follow mods.",
            HelpName = "source",
        };

        Command tracked = new("tracked", "Lists the mods the linked account follows on the site, such as Nexus Mods' tracked mods.") { source };
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
            ? new("track", "Follows the mod on its site with the linked account, as the site's button does, such as Track on Nexus Mods. Following it again changes nothing.") { link }
            : new("untrack", "Stops following the mod on its site, such as Untrack on Nexus Mods. Unfollowing a mod not followed changes nothing.") { link };
        command.SetAction((parsed, cancellationToken) => TrackAsync(environment, parsed.GetValue(link)!, track, cancellationToken));
        return command;
    }

    private static async Task<int> TrackedAsync(CliEnvironment environment, string? sourceText, CancellationToken cancellationToken)
    {
        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    IWatchlists watchlists = services.GetRequiredService<IWatchlists>();
                    if (ChooseSource(watchlists.Sources, sourceText) is not { } source)
                    {
                        await environment.Error.WriteLineAsync(SourceProblem(watchlists.Sources, sourceText, "follow mods")).ConfigureAwait(false);
                        return ExitCodes.BadInput;
                    }

                    ISourceRegistry sources = services.GetRequiredService<ISourceRegistry>();
                    string site = CliText.SiteName(sources, source);
                    string followed = Followed(sources, source);
                    IReadOnlyList<ModRef> tracked;
                    try
                    {
                        tracked = await watchlists.GetWatchedAsync(source, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (ListingProblem(exception, site, followed) is { } problem)
                    {
                        await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                        return problem.ExitCode;
                    }

                    WriteTracked(environment.Output, tracked, site, followed, watchlists.CanChange(source) ? CliText.Words(sources, source).Unfollow ?? "unfollow" : null);
                    return ExitCodes.Done;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> TrackAsync(CliEnvironment environment, string linkText, bool track, CancellationToken cancellationToken)
    {
        if (!ModLinkArgument.TryRead(environment, linkText, out Uri? link))
        {
            return ExitCodes.BadInput;
        }

        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    IWatchlists watchlists = services.GetRequiredService<IWatchlists>();
                    ModRef mod;
                    try
                    {
                        mod = track
                            ? await watchlists.WatchAsync(link, cancellationToken).ConfigureAwait(false)
                            : await watchlists.UnwatchAsync(link, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (TrackingProblem(exception, track ? "follow the mod" : "stop following the mod") is { } problem)
                    {
                        await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                        return problem.ExitCode;
                    }

                    // As the mod's page says it: "Tracked on Nexus Mods."
                    ISourceRegistry sources = services.GetRequiredService<ISourceRegistry>();
                    string on = $"{Followed(sources, mod.Game.Source)} on {CliText.SiteName(sources, mod.Game.Source)}: mod {mod.Id} of {mod.Game.Key}.";
                    await environment.Output.WriteLineAsync(track ? CliText.Capitalized(on) : $"No longer {on}").ConfigureAwait(false);
                    return ExitCodes.Done;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// What the site calls a mod followed, such as "tracked" on Nexus Mods.
    /// </summary>
    private static string Followed(ISourceRegistry sources, SourceId source) => CliText.Words(sources, source).Followed ?? "followed";

    /// <summary>
    /// What to say when the followed mods can't be listed. The question isn't about a link, so the site having no such
    /// list says only that.
    /// </summary>
    /// <param name="site">The site's name, such as Nexus Mods.</param>
    /// <param name="followed">Its word for a mod followed, such as "tracked".</param>
    private static (string Message, int ExitCode)? ListingProblem(Exception exception, string site, string followed) => exception switch
    {
        AuthenticationFailedException => ($"{site} needs a linked account to list its {followed} mods, or refused the one linked. {LinkAccountAgain}", ExitCodes.Failed),
        HttpRequestException or InvalidDataException => ($"{site} couldn't list the {followed} mods: {exception.Message}", ExitCodes.Failed),
        _ => null,
    };

    /// <summary>
    /// What to say when a mod can't be followed or unfollowed. Which site it is isn't known before the link is read, so
    /// it's "the site".
    /// </summary>
    /// <param name="doing">What failed, such as "follow the mod".</param>
    private static (string Message, int ExitCode)? TrackingProblem(Exception exception, string doing) => exception switch
    {
        AuthenticationFailedException => ($"The site needs a linked account to {doing}, or refused the one linked. {LinkAccountAgain}", ExitCodes.Failed),
        HttpRequestException { StatusCode: not (HttpStatusCode.NotFound or HttpStatusCode.Gone) } failed
            => ($"The site couldn't {doing}: {failed.Message}", ExitCodes.Failed),
        _ => LinkProblems.Describe(exception),
    };

    /// <summary>
    /// By game, then by ID, numbers in numeric order, in the site's words: "12 tracked mods on Nexus Mods."
    /// </summary>
    /// <param name="unfollow">The site's word for unfollowing a mod, such as "untrack"; null when ModShelf can't.</param>
    private static void WriteTracked(TextWriter output, IReadOnlyList<ModRef> tracked, string site, string followed, string? unfollow)
    {
        if (tracked.Count == 0)
        {
            output.WriteLine($"No {followed} mods on {site}.");
            return;
        }

        output.WriteLine(tracked.Count == 1 ? $"1 {followed} mod on {site}." : $"{CliText.Count(tracked.Count)} {followed} mods on {site}.");
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
        output.WriteLine(unfollow is null ? $"ModShelf only reads them: change them on {site}'s website." : $"To {unfollow} one: mod untrack \"<its link>\"");
    }
}
