using System.CommandLine;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Catalog;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>mod search [words] --game &lt;game&gt; [--author] [--adult] [--sort] [--page] [--source]</c>.
/// </summary>
internal static partial class ModCommand
{
    /// <summary>
    /// How many mods a page lists.
    /// </summary>
    private const int PageSize = 20;

    private static Command CreateSearch(CliEnvironment environment)
    {
        Argument<string[]> words = new("words")
        {
            Description = "Words to look for in the mods' names. Leave them out to list every mod of the game.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string> game = new("--game")
        {
            Description = "The game, as the site names it in its links, such as stardewvalley on Nexus Mods.",
            HelpName = "game",
            Required = true,
        };
        Option<string?> author = new("--author")
        {
            Description = "Only mods by this author.",
            HelpName = "name",
        };
        Option<bool> adult = new("--adult")
        {
            Description = "Include mods with adult content, which are left out otherwise.",
        };
        Option<SearchSort> sort = new("--sort")
        {
            Description = "The order: Relevance (the default; the most endorsed without words), Endorsements, Downloads, Updated or Newest.",
            DefaultValueFactory = _ => SearchSort.Relevance,
        };
        Option<int> page = new("--page")
        {
            Description = $"Which page of results to show, {PageSize} mods each.",
            DefaultValueFactory = _ => 1,
        };
        Option<string?> source = new("--source")
        {
            Description = "The site to search, such as nexus. Needed only when more than one site can search.",
            HelpName = "source",
        };

        Command search = new("search", "Lists a game's mods that match, a page at a time, each with its link. Nothing is downloaded.")
        {
            words,
            game,
            author,
            adult,
            sort,
            page,
            source,
        };
        search.SetAction((parsed, cancellationToken) =>
        {
            SearchQuery query;
            try
            {
                int pageNumber = parsed.GetValue(page);
                ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1, "--page");
                query = new SearchQuery(
                    string.Join(' ', parsed.GetValue(words) ?? []),
                    parsed.GetValue(sort),
                    checked((pageNumber - 1) * PageSize),
                    PageSize,
                    parsed.GetValue(author),
                    parsed.GetValue(adult));
            }
            catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
            {
                environment.Error.WriteLine("--page must be 1 or more, and not that many pages in.");
                return Task.FromResult(ExitCodes.BadInput);
            }

            return SearchAsync(environment, parsed.GetValue(game)!, parsed.GetValue(source), query, cancellationToken);
        });
        return search;
    }

    private static async Task<int> SearchAsync(
        CliEnvironment environment,
        string gameKey,
        string? sourceText,
        SearchQuery query,
        CancellationToken cancellationToken)
    {
        using IHost host = environment.BuildHost(false);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            IModLookup lookup = host.Services.GetRequiredService<IModLookup>();

            if (ChooseSource(lookup.SearchableSources, sourceText) is not { } source)
            {
                await environment.Error.WriteLineAsync(SourceProblem(lookup.SearchableSources, sourceText)).ConfigureAwait(false);
                return ExitCodes.BadInput;
            }

            SearchPage found;
            try
            {
                // Inside, so a blank game is refused as the source refuses an unknown one.
                found = await lookup.SearchAsync(new GameRef(source, gameKey.Trim()), query, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (SearchProblem(exception) is { } problem)
            {
                await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                return problem.ExitCode;
            }

            WriteResults(environment.Output, found, query);
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
    /// The source asked for, or the only one that can search; null when that's not one of them, or there's no choice.
    /// </summary>
    private static SourceId? ChooseSource(IReadOnlyList<SourceId> searchable, string? asked)
    {
        if (asked is null)
        {
            return searchable.Count == 1 ? searchable[0] : null;
        }

        return searchable.FirstOrDefault(source => string.Equals(source.Value, asked.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string SourceProblem(IReadOnlyList<SourceId> searchable, string? asked) => searchable.Count switch
    {
        0 => "No site ModShelf knows can search.",
        _ when asked is not null => $"'{asked}' can't search. Sites that can: {string.Join(", ", searchable)}.",
        _ => $"More than one site can search; choose one with --source: {string.Join(", ", searchable)}.",
    };

    private static (string Message, int ExitCode)? SearchProblem(Exception exception) => exception switch
    {
        ArgumentException refused => (refused.MessageForUser(), ExitCodes.BadInput),
        AuthenticationFailedException => (
            "The site needs a linked account to search, or refused the one linked. Link it with account link, then run "
            + "the same command again.",
            ExitCodes.Failed),
        HttpRequestException failed => ($"The site couldn't search: {failed.Message}", ExitCodes.Failed),
        _ => null,
    };

    private static void WriteResults(TextWriter output, SearchPage found, SearchQuery query)
    {
        if (found.Mods.Count == 0)
        {
            output.WriteLine(found.TotalCount == 0
                ? "No mods match."
                : $"No mods on that page: {Count(found.TotalCount)} match, {PageSize} to a page.");
            return;
        }

        int first = found.Offset + 1;
        int last = found.Offset + found.Mods.Count;
        string matches = found.TotalCount == 1 ? "1 mod matches" : $"{Count(found.TotalCount)} mods match";
        output.WriteLine(found.Mods.Count == 1 && first == found.TotalCount
            ? $"{matches}{SortedBy(query)}."
            : $"{matches}{SortedBy(query)}. These are {Count(first)} to {Count(last)}.");

        string[][] rows =
        [
            ["ID", "Name", "Author", "Endorsements", "Downloads", "Updated", "Link"],
            .. found.Mods.Select(mod => new[]
            {
                mod.Mod.Id,
                Label(mod),
                mod.Author ?? "-",
                mod.Endorsements is { } endorsements ? Count(endorsements) : "-",
                mod.Downloads is { } downloads ? Count(downloads) : "-",
                mod.UpdatedAt is { } updated ? Date(updated) : "-",
                mod.PageUrl?.AbsoluteUri ?? "-",
            }),
        ];
        TextTable.Write(output, rows);

        if (found.HasMore)
        {
            output.WriteLine($"More on the next page: add --page {(found.Offset / PageSize) + 2}.");
        }

        output.WriteLine("To see a mod's files: mod show \"<its link>\"");
    }

    private static string SortedBy(SearchQuery query) => query.Sort switch
    {
        SearchSort.Relevance when query.Text is not null => ", best matches first",
        SearchSort.Relevance or SearchSort.Endorsements => ", most endorsed first",
        SearchSort.Downloads => ", most downloaded first",
        SearchSort.Updated => ", last updated first",
        SearchSort.Newest => ", newest first",
        _ => string.Empty,
    };

    /// <summary>
    /// The mod's name, with what to know before opening it.
    /// </summary>
    private static string Label(ModInfo mod)
        => mod.Name + (mod.HasAdultContent ? " (adult)" : string.Empty) + (mod.IsAvailable ? string.Empty : " (hidden)");

    private static string Count(int count) => count.ToString("N0", CultureInfo.CurrentCulture);
}
