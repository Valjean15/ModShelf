using System.CommandLine;
using System.Globalization;
using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Updates;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>mod updates --game &lt;game&gt; [--since 1d|7d|28d|&lt;date&gt;] [--source]</c>.
/// </summary>
internal static partial class ModCommand
{
    /// <summary>
    /// What --since takes besides a date: how many days back, as the update feeds offer them.
    /// </summary>
    private static readonly Dictionary<string, int> SinceDays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1d"] = 1,
        ["7d"] = 7,
        ["28d"] = 28,
    };

    /// <summary>
    /// The dates and times --since takes: the ISO 8601 ones, and the way the command writes times.
    /// </summary>
    private static readonly string[] SinceFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
    ];

    private static Command CreateUpdates(CliEnvironment environment)
    {
        Option<string> game = new("--game")
        {
            Description = "The game, as the site names it in its links, such as stardewvalley on Nexus Mods.",
            HelpName = "game",
            Required = true,
        };
        Option<string> since = new("--since")
        {
            Description = "How far back to look: 1d, 7d or 28d, or a date or time, such as 2026-10-20 or 2026-10-20T18:00. 1d unless set.",
            HelpName = "when",
            DefaultValueFactory = _ => "1d",
        };
        Option<string?> source = new("--source")
        {
            Description = "The site to ask, such as nexus. Needed only when more than one site lists updates.",
            HelpName = "source",
        };

        Command updates = new("updates", "Lists a game's mods that changed lately, each with its link, in one request. Nothing is downloaded.")
        {
            game,
            since,
            source,
        };
        updates.SetAction((parsed, cancellationToken)
            => UpdatesAsync(environment, parsed.GetValue(game)!, parsed.GetValue(since)!, parsed.GetValue(source), cancellationToken));
        return updates;
    }

    private static async Task<int> UpdatesAsync(
        CliEnvironment environment,
        string gameKey,
        string sinceText,
        string? sourceText,
        CancellationToken cancellationToken)
    {
        using IHost host = environment.BuildHost(false);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            IModUpdates feeds = host.Services.GetRequiredService<IModUpdates>();

            DateTimeOffset now = host.Services.GetRequiredService<TimeProvider>().GetUtcNow();
            if (ReadSince(sinceText, now) is not { } since)
            {
                await environment.Error.WriteLineAsync(
                    "--since takes 1d, 7d or 28d, or a date or time, such as 2026-10-20 or 2026-10-20T18:00.").ConfigureAwait(false);
                return ExitCodes.BadInput;
            }

            if (since > now)
            {
                await environment.Error.WriteLineAsync("--since must be in the past.").ConfigureAwait(false);
                return ExitCodes.BadInput;
            }

            if (ChooseSource(feeds.Sources, sourceText) is not { } chosen)
            {
                await environment.Error.WriteLineAsync(SourceProblem(feeds.Sources, sourceText, "list updates")).ConfigureAwait(false);
                return ExitCodes.BadInput;
            }

            string key = gameKey.Trim();
            IReadOnlyList<ModUpdate> found;
            try
            {
                found = await feeds.GetUpdatesAsync(new GameRef(chosen, key), since, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (UpdatesProblem(exception, key) is { } problem)
            {
                await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                return problem.ExitCode;
            }

            WriteUpdates(environment.Output, found, key, since);
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
    /// 1d, 7d or 28d back from <paramref name="now"/>; or a date, meaning its start in local time; or a date and time,
    /// local unless it says otherwise. Null for anything else.
    /// </summary>
    private static DateTimeOffset? ReadSince(string text, DateTimeOffset now)
    {
        string trimmed = text.Trim();
        if (SinceDays.TryGetValue(trimmed, out int days))
        {
            return now.AddDays(-days);
        }

        return DateTimeOffset.TryParseExact(trimmed, SinceFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset since)
            ? since
            : null;
    }

    private static (string Message, int ExitCode)? UpdatesProblem(Exception exception, string game) => exception switch
    {
        ArgumentException refused => (refused.MessageForUser(), ExitCodes.BadInput),
        AuthenticationFailedException => (
            "The site needs a linked account to list its updates, or refused the one linked. Link it with account link, then "
            + "run the same command again.",
            ExitCodes.Failed),
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => ($"The site has no game '{game}'.", ExitCodes.BadInput),
        HttpRequestException failed => ($"The site couldn't list its updates: {failed.Message}", ExitCodes.Failed),
        _ => null,
    };

    private static void WriteUpdates(TextWriter output, IReadOnlyList<ModUpdate> found, string game, DateTimeOffset since)
    {
        string from = Time(since);
        if (found.Count == 0)
        {
            output.WriteLine($"No mods of {game} changed since {from}.");
            return;
        }

        int withFiles = found.Count(update => update.FilesUpdatedAt >= since);
        output.WriteLine(found.Count == 1
            ? $"1 mod of {game} changed since {from}."
            : $"{Count(found.Count)} mods of {game} changed since {from}, the most recently active first.");

        string[][] rows =
        [
            ["", "ID", "Files updated", "Last active", "Link"],
            .. found.Select(update => new[]
            {
                update.FilesUpdatedAt >= since ? "*" : "",
                update.Mod.Id,
                update.FilesUpdatedAt is { } files ? Time(files) : "-",
                Time(update.LastActivityAt),
                update.PageUrl?.AbsoluteUri ?? "-",
            }),
        ];
        TextTable.Write(output, rows);

        output.WriteLine((withFiles, found.Count) switch
        {
            (0, 1) => "Its files didn't change since then, only its page, such as its description or comments.",
            (0, _) => "None of their files changed since then, only their pages, such as their descriptions or comments.",
            _ when withFiles == found.Count => "* New or changed files since then.",
            _ => "* New or changed files since then. The other mods' pages changed, such as their descriptions or comments.",
        });
        output.WriteLine("To see a mod's files: mod show \"<its link>\"");
    }

    private static string Time(DateTimeOffset moment)
        => moment.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
