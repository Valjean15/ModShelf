using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Catalog;
using ModShelf.Application.Sources;
using ModShelf.Cli.Common;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>mod search [words] --game &lt;game&gt; [--author] [--adult] [--sort] [--ascending] [--page] [--page-size] [--source]</c>.
/// </summary>
internal static partial class ModCommand
{
    /// <summary>
    /// How many mods a page lists unless --page-size says otherwise, or fewer when the site's pages hold fewer.
    /// </summary>
    private const int DefaultPageSize = 20;

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
        Option<string?> category = new("--category")
        {
            Description = "Only mods in this category of the game, by its name in any case, where the site can search by category. One the game doesn't have is refused, naming those it has.",
            HelpName = "name",
        };
        Option<int?> updatedWithin = new("--updated-within")
        {
            Description = $"Only mods updated within this many days, from 1 to {SearchArguments.MostDays}, such as 7, where the site can search by when mods were updated. The days count from the start of the hour.",
            HelpName = "days",
        };
        Option<bool> adult = new("--adult")
        {
            Description = "Include mods with adult content, which are left out otherwise.",
        };
        Option<SearchSort?> sort = new("--sort")
        {
            Description = "The order, as far as the site has it: Relevance, Endorsements (whatever the site counts as approval), Downloads, UniqueDownloads, Updated, Newest, Size or Name. The site's own unless set: Relevance on Nexus Mods, which without words is the site's own order. The most comes first; by Name, Z first.",
        };
        sort.Validators.Add(result =>
        {
            // An enum option reads a number too, such as 9, which needn't be one of the orders.
            if (result.GetValueOrDefault<SearchSort?>() is { } value && !Enum.IsDefined(value))
            {
                result.AddError($"--sort must be one of {string.Join(", ", Enum.GetNames<SearchSort>())}.");
            }
        });
        Option<bool> ascending = new("--ascending")
        {
            Description = "Turn the order around, so the least comes first: the least approved or downloaded, the longest since updated, the oldest.",
        };
        Option<int> page = new("--page")
        {
            Description = "Which page of results to show.",
            DefaultValueFactory = _ => 1,
        };
        Option<int?> pageSize = new("--page-size")
        {
            Description = $"How many mods a page lists, from 1 to {SearchQuery.MaxCount}, as many as the site's pages hold at most. {DefaultPageSize} unless set, or fewer when the site's pages hold fewer.",
            HelpName = "count",
        };
        Option<string?> source = new("--source")
        {
            Description = "The site to search, such as nexus. Needed only when the game's key could be more than one site's.",
            HelpName = "source",
        };

        Command search = new("search", "Lists a game's mods that match, a page at a time, each with its link. Nothing is downloaded.")
        {
            words,
            game,
            author,
            category,
            updatedWithin,
            adult,
            sort,
            ascending,
            page,
            pageSize,
            source,
        };
        search.SetAction((parsed, cancellationToken) =>
        {
            int? size = parsed.GetValue(pageSize);
            if (size is < 1 or > SearchQuery.MaxCount)
            {
                environment.Error.WriteLine($"--page-size must be from 1 to {SearchQuery.MaxCount}.");
                return Task.FromResult(ExitCodes.BadInput);
            }

            int? days = parsed.GetValue(updatedWithin);
            if (days is < 1 or > SearchArguments.MostDays)
            {
                environment.Error.WriteLine($"--updated-within must be from 1 to {SearchArguments.MostDays} days.");
                return Task.FromResult(ExitCodes.BadInput);
            }

            int pageNumber = parsed.GetValue(page);
            string text = string.Join(' ', parsed.GetValue(words) ?? []);
            SearchSort? sortBy = parsed.GetValue(sort);
            bool reversed = parsed.GetValue(ascending);
            string? by = parsed.GetValue(author);
            bool withAdult = parsed.GetValue(adult);

            // The site's own order and page size unless asked otherwise, once the site is known; a whole number of pages
            // in, so each page starts where the last ended. The category as the game names it.
            SearchQuery QueryFor(SearchCapabilities? capabilities, string? categoryNamed, DateTimeOffset now)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1, "--page");
                SearchOrder own = capabilities?.Orders[0] ?? new SearchOrder(SearchSort.Relevance, SearchDirection.Descending);
                int count = size ?? Math.Min(DefaultPageSize, capabilities?.MaxCount ?? DefaultPageSize);
                SearchDirection direction = sortBy is null ? own.Direction : SearchDirection.Descending;
                return new SearchQuery(
                    text,
                    sortBy ?? own.Sort,
                    checked((pageNumber - 1) * count),
                    count,
                    by,
                    withAdult,
                    reversed ? Reverse(direction) : direction,
                    categoryNamed,
                    days is { } within ? SearchArguments.UpdatedSince(now, within) : null);
            }

            return SearchAsync(environment, parsed.GetValue(game)!, parsed.GetValue(source), parsed.GetValue(category), QueryFor, cancellationToken);
        });
        return search;
    }

    private static SearchDirection Reverse(SearchDirection direction)
        => direction == SearchDirection.Ascending ? SearchDirection.Descending : SearchDirection.Ascending;

    /// <param name="category">The category asked for, as typed.</param>
    /// <param name="queryFor">The search to make, for what the chosen site's search can do, with the category as the game
    /// names it, at the time it is now.</param>
    private static async Task<int> SearchAsync(
        CliEnvironment environment,
        string gameKey,
        string? sourceText,
        string? category,
        Func<SearchCapabilities?, string?, DateTimeOffset, SearchQuery> queryFor,
        CancellationToken cancellationToken)
    {
        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    IModLookup lookup = services.GetRequiredService<IModLookup>();
                    ISourceRegistry sources = services.GetRequiredService<ISourceRegistry>();

                    // Without a site named, those whose games' keys look like the game's could have it.
                    IReadOnlyList<SourceId> able = sourceText is null
                        ? GameSites.ThatCouldHave(gameKey, lookup.SearchableSources, sources)
                        : lookup.SearchableSources;
                    if (ChooseSource(able, sourceText) is not { } source)
                    {
                        await environment.Error.WriteLineAsync(SourceProblem(able, sourceText, "search")).ConfigureAwait(false);
                        return ExitCodes.BadInput;
                    }

                    IModSearch? search = sources.Find<IModSearch>(source);
                    SearchQuery query;
                    SearchPage found;
                    try
                    {
                        // Inside, so a blank game is refused as the source refuses an unknown one. A category is the
                        // game's, as the site names it; a site that can't search by category refuses it, as Application
                        // does.
                        GameRef searched = new(source, gameKey.Trim());
                        if (category is not null
                            && search?.Capabilities.Has(SearchFilters.Category) == true
                            && sources.Find<IModCatalog>(source) is { } catalog)
                        {
                            category = SearchArguments.Category(category, await catalog.GetGameAsync(searched, cancellationToken).ConfigureAwait(false), search.DisplayName);
                        }

                        try
                        {
                            query = queryFor(search?.Capabilities, category, services.GetRequiredService<TimeProvider>().GetUtcNow());
                        }
                        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
                        {
                            await environment.Error.WriteLineAsync("--page must be 1 or more, and not that many pages in.").ConfigureAwait(false);
                            return ExitCodes.BadInput;
                        }

                        found = await lookup.SearchAsync(searched, query, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (SearchProblem(exception) is { } problem)
                    {
                        await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                        return problem.ExitCode;
                    }

                    WriteResults(environment.Output, found, query, CliText.Words(sources, source));
                    return ExitCodes.Done;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The source asked for, or the only one that can; null when that's not one of them, or there's no choice.
    /// </summary>
    private static SourceId? ChooseSource(IReadOnlyList<SourceId> able, string? asked)
    {
        if (asked is null)
        {
            return able.Count == 1 ? able[0] : null;
        }

        return able.FirstOrDefault(source => string.Equals(source.Value, asked.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <param name="what">What the sources can do, such as "search".</param>
    private static string SourceProblem(IReadOnlyList<SourceId> able, string? asked, string what) => able.Count switch
    {
        0 => $"No site ModShelf knows can {what}.",
        _ when asked is not null => $"'{asked}' can't {what}. Sites that can: {string.Join(", ", able)}.",
        _ => $"More than one site can {what}; choose one with --source: {string.Join(", ", able)}.",
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

    /// <summary>
    /// Such as ", but 2 with adult content are left out (add --adult to see them)", for the adult mods a site marks but
    /// can't leave out, which ModShelf left out of the page.
    /// </summary>
    private static string LeftOut(SearchPage found)
        => found.LeftOut switch
        {
            0 => string.Empty,
            1 => ", but 1 with adult content is left out (add --adult to see it)",
            _ => $", but {CliText.Count(found.LeftOut)} with adult content are left out (add --adult to see them)",
        };

    /// <param name="words">The site's words, for what it counts as approval; without them, there's no column for it.</param>
    private static void WriteResults(TextWriter output, SearchPage found, SearchQuery query, SiteWords words)
    {
        if (found.Mods.Count == 0 && found.LeftOut == 0)
        {
            output.WriteLine(found.TotalCount == 0
                ? "No mods match."
                : $"No mods on that page: {CliText.Count(found.TotalCount)} match, {CliText.Count(query.Count)} to a page.");
            return;
        }

        int first = found.Offset + 1;
        int last = found.NextOffset;
        string matches = found.TotalCount == 1 ? "1 mod matches" : $"{CliText.Count(found.TotalCount)} mods match";
        output.WriteLine(found.Mods.Count == 1 && found.LeftOut == 0 && first == found.TotalCount
            ? $"{matches}{SortedBy(query, words)}."
            : $"{matches}{SortedBy(query, words)}. These are {CliText.Count(first)} to {CliText.Count(last)}{LeftOut(found)}.");

        if (found.Mods.Count > 0)
        {
            WriteTable(output, found, words);
        }

        if (found.HasMore)
        {
            output.WriteLine($"More on the next page: add --page {(found.Offset / query.Count) + 2}.");
        }

        if (found.Mods.Count > 0)
        {
            output.WriteLine("To see a mod's files: mod show \"<its link>\"");
        }
    }

    /// <summary>
    /// The page's mods, a row each.
    /// </summary>
    private static void WriteTable(TextWriter output, SearchPage found, SiteWords words)
    {
        string? approvals = words.Approvals;
        string[][] rows =
        [
            ["ID", "Name", "Author", .. approvals is null ? [] : new[] { CliText.Capitalized(approvals) }, "Downloads", "Updated", "Link"],
            .. found.Mods.Select<ModInfo, string[]>(mod =>
            [
                mod.Mod.Id,
                Label(mod),
                mod.Author ?? "-",
                .. approvals is null ? [] : new[] { mod.Endorsements is { } endorsements ? CliText.Count(endorsements) : "-" },
                mod.Downloads is { } downloads ? CliText.Count(downloads) : "-",
                mod.UpdatedAt is { } updated ? CliText.Date(updated) : "-",
                mod.PageUrl?.AbsoluteUri ?? "-",
            ]),
        ];
        TextTable.Write(output, rows);
    }

    /// <summary>
    /// Such as ", most downloaded first"; approval in the site's words. Relevance without words is the site's own order,
    /// which isn't said.
    /// </summary>
    private static string SortedBy(SearchQuery query, SiteWords words) => (query.Sort, query.Direction) switch
    {
        (SearchSort.Relevance, SearchDirection.Descending) when query.Text is not null => ", best matches first",
        (SearchSort.Relevance, SearchDirection.Ascending) when query.Text is not null => ", weakest matches first",
        (SearchSort.Relevance, SearchDirection.Descending) => ", in the site's own order",
        (SearchSort.Relevance, SearchDirection.Ascending) => ", in the site's own order turned around",
        (SearchSort.Endorsements, SearchDirection.Descending) => $", most {words.Approvals ?? "approvals"} first",
        (SearchSort.Endorsements, SearchDirection.Ascending) => $", fewest {words.Approvals ?? "approvals"} first",
        (SearchSort.Downloads, SearchDirection.Descending) => ", most downloaded first",
        (SearchSort.Downloads, SearchDirection.Ascending) => ", least downloaded first",
        (SearchSort.Updated, SearchDirection.Descending) => ", last updated first",
        (SearchSort.Updated, SearchDirection.Ascending) => ", longest since updated first",
        (SearchSort.Newest, SearchDirection.Descending) => ", newest first",
        (SearchSort.Newest, SearchDirection.Ascending) => ", oldest first",
        (SearchSort.Name, SearchDirection.Descending) => ", by name from Z to A",
        (SearchSort.Name, SearchDirection.Ascending) => ", by name from A to Z",
        (SearchSort.Size, SearchDirection.Descending) => ", largest first",
        (SearchSort.Size, SearchDirection.Ascending) => ", smallest first",
        (SearchSort.UniqueDownloads, SearchDirection.Descending) => ", most downloaded by different users first",
        (SearchSort.UniqueDownloads, SearchDirection.Ascending) => ", least downloaded by different users first",
        _ => string.Empty,
    };

    /// <summary>
    /// The mod's name, with what to know before opening it.
    /// </summary>
    private static string Label(ModInfo mod)
        => mod.Name + (mod.HasAdultContent ? " (adult)" : string.Empty) + (mod.IsAvailable ? string.Empty : " (hidden)");

}
