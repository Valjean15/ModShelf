using System.CommandLine;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Catalog;
using ModShelf.Application.Common;
using ModShelf.Application.Sources;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>mod games [words] [--source] [--page] [--page-size]</c>: a site's games whose names match, each with the key
/// <c>--game</c> takes.
/// </summary>
internal static partial class ModCommand
{
    /// <summary>
    /// How many games a page lists unless --page-size says otherwise.
    /// </summary>
    private const int DefaultGamesPageSize = 20;

    /// <summary>
    /// The most games a page lists.
    /// </summary>
    private const int MostGamesAPage = 100;

    private static Command CreateGames(CliEnvironment environment)
    {
        Argument<string[]> words = new("words")
        {
            Description = "Words of the game's name, each the start of one of its words, in any order: \"stalker 2\" finds S.T.A.L.K.E.R. 2. Leave them out to list every game.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string?> source = new("--source")
        {
            Description = "The site, nexus or gamebanana. Needed only when more than one site lists its games.",
            HelpName = "source",
        };
        Option<int> page = new("--page")
        {
            Description = "Which page of games to show.",
            DefaultValueFactory = _ => 1,
        };
        Option<int> pageSize = new("--page-size")
        {
            Description = $"How many games a page lists, from 1 to {MostGamesAPage}. {DefaultGamesPageSize} unless set.",
            HelpName = "count",
            DefaultValueFactory = _ => DefaultGamesPageSize,
        };

        Command games = new("games", "Lists a site's games whose names match, those that start with the words first, each with the key mod search's --game takes.")
        {
            words,
            source,
            page,
            pageSize,
        };
        games.SetAction((parsed, cancellationToken) =>
        {
            int size = parsed.GetValue(pageSize);
            if (size is < 1 or > MostGamesAPage)
            {
                environment.Error.WriteLine($"--page-size must be from 1 to {MostGamesAPage}.");
                return Task.FromResult(ExitCodes.BadInput);
            }

            int pageNumber = parsed.GetValue(page);
            if (pageNumber < 1 || (long)(pageNumber - 1) * size > int.MaxValue)
            {
                environment.Error.WriteLine("--page must be 1 or more, and not that many pages in.");
                return Task.FromResult(ExitCodes.BadInput);
            }

            NameQuery query = NameQuery.Parse(string.Join(' ', parsed.GetValue(words) ?? []));
            return GamesAsync(environment, query, parsed.GetValue(source), (pageNumber - 1) * size, size, cancellationToken);
        });
        return games;
    }

    private static async Task<int> GamesAsync(
        CliEnvironment environment,
        NameQuery query,
        string? sourceText,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    ISourceRegistry sources = services.GetRequiredService<ISourceRegistry>();
                    SourceId[] able = [.. sources.All<IModCatalog>().Select(catalog => catalog.Id)];
                    if (ChooseSource(able, sourceText) is not { } source)
                    {
                        await environment.Error.WriteLineAsync(SourceProblem(able, sourceText, "list its games")).ConfigureAwait(false);
                        return ExitCodes.BadInput;
                    }

                    IReadOnlyList<GameInfo> games;
                    try
                    {
                        games = await services.GetRequiredService<IModLookup>().GetGamesAsync(source, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (GamesProblem(exception) is { } problem)
                    {
                        await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                        return problem.ExitCode;
                    }

                    string site = sources.Find<IModSource>(source)?.DisplayName ?? source.Value;
                    StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
                    IReadOnlyList<GameInfo> found = query.Find(games.OrderBy(game => game.Name, byName), game => game.Name);
                    WriteGames(environment.Output, found, query, site, offset, count);
                    return ExitCodes.Done;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static (string Message, int ExitCode)? GamesProblem(Exception exception) => exception switch
    {
        ArgumentException refused => (refused.MessageForUser(), ExitCodes.BadInput),
        AuthenticationFailedException => (
            "The site needs a linked account to list its games, or refused the one linked. Link it with account link, then "
            + "run the same command again.",
            ExitCodes.Failed),
        HttpRequestException failed => ($"The site couldn't list its games: {failed.Message}", ExitCodes.Failed),
        _ => null,
    };

    private static void WriteGames(TextWriter output, IReadOnlyList<GameInfo> found, NameQuery query, string site, int offset, int count)
    {
        if (found.Count == 0)
        {
            output.WriteLine(query.IsEmpty ? $"{site} lists no games." : $"No game on {site} is named so: \"{query.Text}\".");
            return;
        }

        GameInfo[] shown = [.. found.Skip(offset).Take(count)];
        string matches = query.IsEmpty
            ? $"{site} lists {CliText.Count(found.Count, "game", "games")}"
            : found.Count == 1 ? $"1 game on {site} matches" : $"{CliText.Count(found.Count)} games on {site} match";
        if (shown.Length == 0)
        {
            output.WriteLine($"No games on that page: {matches}, {CliText.Count(count)} to a page.");
            return;
        }

        output.WriteLine(found.Count == 1 ? $"{matches}." : $"{matches}. These are {CliText.Count(offset + 1)} to {CliText.Count(offset + shown.Length)}.");
        TextTable.Write(output, [["Key", "Name"], .. shown.Select(game => new[] { game.Game.Key, game.Name })]);
        if (offset + shown.Length < found.Count)
        {
            output.WriteLine($"More on the next page: add --page {(offset / count) + 2}.");
        }

        output.WriteLine("To search a game's mods: mod search --game <its key>");
    }
}
