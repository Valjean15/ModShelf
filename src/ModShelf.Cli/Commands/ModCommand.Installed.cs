using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Catalog;
using ModShelf.Application.Common;
using ModShelf.Application.Installed;
using ModShelf.Application.Sources;
using ModShelf.Cli.Common;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>mod games --installed [words] [--source] [--page] [--page-size]</c>: the games installed on this computer, each with
/// its games on the sites.
/// </summary>
internal static partial class ModCommand
{
    private static async Task<int> InstalledAsync(
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
                    SourceId? source = null;
                    if (sourceText is not null)
                    {
                        SourceId[] able = [.. sources.All<IModCatalog>().Select(catalog => catalog.Id)];
                        source = ChooseSource(able, sourceText);
                        if (source is null)
                        {
                            await environment.Error.WriteLineAsync(SourceProblem(able, sourceText, "list its games")).ConfigureAwait(false);
                            return ExitCodes.BadInput;
                        }
                    }

                    InstalledGameListing listing = await InstalledGameList.ReadAsync(
                            services.GetRequiredService<IModLookup>(),
                            sources,
                            services.GetRequiredService<IInstalledGameMatcher>(),
                            source,
                            query,
                            cancellationToken)
                        .ConfigureAwait(false);
                    foreach (UnreadSite unread in listing.Unread)
                    {
                        await environment.Error.WriteLineAsync($"{unread.Name} couldn't list its games, so they match only as chosen: {Unread(unread.Problem)}").ConfigureAwait(false);
                    }

                    WriteInstalled(environment.Output, listing, query, offset, count);
                    return ExitCodes.Done;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Unread(Exception problem) => problem switch
    {
        AuthenticationFailedException => "it needs a linked account, or refused the one linked.",
        _ => problem.Message,
    };

    private static void WriteInstalled(TextWriter output, InstalledGameListing listing, NameQuery query, int offset, int count)
    {
        if (listing.Stores.Count == 0)
        {
            output.WriteLine("Steam isn't on this computer, so there are no installed games.");
            return;
        }

        string stores = string.Join(" and ", listing.Stores.Select(store => store.Name));
        if (listing.Games.Count == 0)
        {
            output.WriteLine(query.IsEmpty ? $"{stores} has no games installed." : $"No installed game is named so: \"{query.Text}\".");
            return;
        }

        InstalledGameMatch[] shown = [.. listing.Games.Skip(offset).Take(count)];
        int withGame = listing.Games.Count(game => game.Sites.Any(site => site.Games.Count > 0));
        string matches = query.IsEmpty
            ? $"{stores} has {CliText.Count(listing.Games.Count, "game", "games")} installed, {CliText.Count(withGame)} with a site's game"
            : listing.Games.Count == 1 ? "1 installed game matches" : $"{CliText.Count(listing.Games.Count)} installed games match";
        if (shown.Length == 0)
        {
            output.WriteLine($"No installed games on that page: {matches}, {CliText.Count(count)} to a page.");
            return;
        }

        output.WriteLine(listing.Games.Count <= count && offset == 0
            ? $"{matches}."
            : $"{matches}. These are {CliText.Count(offset + 1)} to {CliText.Count(offset + shown.Length)}.");
        TextTable.Write(output, [["Game", "Store", "Site", "Key", "Match"], .. shown.SelectMany(Rows)]);
        if (offset + shown.Length < listing.Games.Count)
        {
            output.WriteLine($"More on the next page: add --page {(offset / count) + 2}.");
        }

        output.WriteLine("To search a game's mods: mod search --game <its key> --source <its site>");
        output.WriteLine("Choose an installed game's games in the desktop app's Settings › Games.");
    }

    /// <summary>
    /// An installed game's rows: one per game matched on a site, or a site said to have none; one alone when no site has
    /// a game for it. Its name and store only on the first.
    /// </summary>
    private static List<string[]> Rows(InstalledGameMatch game)
    {
        List<string[]> rows = [];
        foreach (SiteMatch site in game.Sites.Where(site => site.Picked || site.Games.Count > 0))
        {
            string match = (site.Picked, site.Games.Count) switch
            {
                (true, 0) => "none, chosen",
                (true, _) => "chosen",
                (false, 1) => "same name",
                _ => "one of the same name",
            };
            if (site.Games.Count == 0)
            {
                rows.Add([string.Empty, string.Empty, site.Site.Value, "-", match]);
            }

            rows.AddRange(site.Games.Select(found => new[] { string.Empty, string.Empty, site.Site.Value, found.Game.Key, match }));
        }

        if (rows.Count == 0)
        {
            rows.Add([string.Empty, string.Empty, "-", "-", "no site's game"]);
        }

        rows[0][0] = game.Game.Name;
        rows[0][1] = $"{game.Game.Store} {game.Game.Id}";
        return rows;
    }
}
