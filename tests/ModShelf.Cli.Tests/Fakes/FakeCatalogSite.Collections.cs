using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Collections;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// Its collections, at <c>https://catalog.example/game/collections/{slug}</c>. Any slug is "Essentials", revision 3:
/// mod 1's newest main file and its optional textures, a file it no longer offers, and a script extender it needs from
/// elsewhere. But "gone" is one it doesn't have, "locked" needs an account, "broken" fails, and "bad" can't be read.
/// </summary>
internal sealed partial class FakeCatalogSite : IModCollections
{
    public static Uri CollectionLink(string slug) => new($"https://{Host}/game/collections/{slug}");

    public Task<CollectionRevision?> GetCollectionAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.Host != Host || link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) is not [_, "collections", string slug])
        {
            return Task.FromResult<CollectionRevision?>(null);
        }

        return slug switch
        {
            "gone" => Task.FromException<CollectionRevision?>(new HttpRequestException("Not found.", null, HttpStatusCode.NotFound)),
            "locked" => Task.FromException<CollectionRevision?>(new AuthenticationFailedException("The site wants an account first.")),
            "broken" => Task.FromException<CollectionRevision?>(new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable)),
            "bad" => Task.FromException<CollectionRevision?>(new ArgumentException("That collection link names no collection.", nameof(link))),
            _ => Task.FromResult<CollectionRevision?>(new CollectionRevision(
                "Essentials",
                new GameRef(SourceId, "game"),
                link,
                3,
                [
                    new CollectionFile(File("1", NewestMainFile), "Example Mod 1", "Example Mod 2.0", "2.0", ModFileCategory.Main),
                    new CollectionFile(File("1", "20"), "Example Mod 1", "Extra textures", null, ModFileCategory.Optional, optional: true),
                ],
                [new CollectionResource("Script extender", new Uri("https://extender.example/"))],
                filesUnavailable: 1)),
        };
    }
}
