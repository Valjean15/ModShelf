using System.Net;
using System.Web;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A mod site with a catalog and a search, at <c>https://catalog.example/game/mods/{mod}[?file_id={file}]</c>. Mod 1
/// has files in every section; mod 2 is hidden and has none. A link without a file means mod 1's newest main file, 12.
/// Links to mod <c>404</c> are to a mod it doesn't have, and to mod <c>401</c> need an account it doesn't have.
/// </summary>
/// <remarks>
/// Its update feed is in <c>FakeCatalogSite.Updates.cs</c>.
/// <para/>
/// Its one game, <c>game</c>, has 45 mods to search, "Example Mod 1" to "Example Mod 45" with IDs 101 to 145, by
/// "Someone" when even and "Another" when odd. Mod 103 has adult content and mod 104 is hidden. A search matches names
/// that hold every word; searching for "broken" fails, and for "locked" needs an account.
/// </remarks>
internal sealed partial class FakeCatalogSite : ILinkRecognizer, IModCatalog, IModSearch
{
    public const string NewestMainFile = "12";

    private const string Host = "catalog.example";

    private static readonly DateTimeOffset January = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static SourceId SourceId { get; } = new("fake-catalog");

    public SourceId Id => SourceId;

    public string DisplayName => "Fake catalog site";

    public static Uri ModLink(string mod) => new($"https://{Host}/game/mods/{mod}");

    public static Uri FileLink(string mod, string file) => new($"https://{Host}/game/mods/{mod}?file_id={file}");

    public Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.Host != Host)
        {
            return Task.FromResult<RecognizedLink?>(null);
        }

        string mod = link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
        Fail(mod);
        string file = HttpUtility.ParseQueryString(link.Query)["file_id"] ?? NewestMainFile;
        return Task.FromResult<RecognizedLink?>(new RecognizedLink(File(mod, file), $"file-{file}.zip"));
    }

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
        => Task.FromResult(new GameInfo(game, "Example Game", new Uri($"https://{Host}/game")));

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        return Task.FromResult(modRef.Id == "2"
            ? new ModInfo(modRef, "Hidden Mod", isAvailable: false)
            : new ModInfo(
                modRef,
                "Example Mod",
                "Makes the example better.",
                "Someone",
                "2.0",
                ModLink(modRef.Id),
                updatedAt: January.AddDays(30),
                hasAdultContent: true,
                downloads: 1234567,
                endorsements: 8910));
    }

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        if (modRef.Id == "2")
        {
            return Task.FromResult<IReadOnlyList<ModFile>>([]);
        }

        return Task.FromResult<IReadOnlyList<ModFile>>(
        [
            Listed(modRef, "11", "Example Mod 1.0", "1.0", ModFileCategory.Main, days: 0),
            Listed(modRef, NewestMainFile, "Example Mod 2.0", "2.0", ModFileCategory.Main, days: 30),
            Listed(modRef, "20", "Extra textures", null, ModFileCategory.Optional, days: 31),
            Listed(modRef, "30", "Example Mod 0.9", "0.9", ModFileCategory.OldVersion, days: -30),
            Listed(modRef, "40", "Example Mod 0.1", "0.1", ModFileCategory.Archived, days: -60),
            Listed(modRef, "50", "Notes", null, ModFileCategory.Unknown, days: -90),
        ]);
    }

    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ModChangelog>>(
        [
            new ModChangelog("1.0", ["First release."]),
            new ModChangelog("2.0", ["Faster loading.", "Fixed the crash on start."]),
        ]);

    /// <summary>
    /// The last query searched, for checking how the command asked.
    /// </summary>
    public SearchQuery? LastQuery { get; private set; }

    public Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(query);
        LastQuery = query;
        if (game.Source != SourceId || game.Key != "game")
        {
            throw new ArgumentException($"The site has no game '{game.Key}'.", nameof(game));
        }

        switch (query.Text)
        {
            case "broken":
                throw new HttpRequestException("Field 'nameStemmed' is not defined.");
            case "locked":
                throw new AuthenticationFailedException("The site wants an account first.");
            default:
                break;
        }

        string[] words = query.Text?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        ModInfo[] matches =
        [
            .. Enumerable.Range(1, 45)
                .Select(SearchableMod)
                .Where(mod => words.All(word => mod.Name.Split(' ').Contains(word, StringComparer.OrdinalIgnoreCase)))
                .Where(mod => query.Author is null || string.Equals(mod.Author, query.Author, StringComparison.OrdinalIgnoreCase))
                .Where(mod => query.IncludeAdultContent || !mod.HasAdultContent),
        ];
        return Task.FromResult(new SearchPage(matches.Skip(query.Offset).Take(query.Count), matches.Length, query.Offset));
    }

    private static ModInfo SearchableMod(int number)
    {
        string id = (100 + number).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new ModInfo(
            new ModRef(new GameRef(SourceId, "game"), id),
            $"Example Mod {number}",
            author: number % 2 == 0 ? "Someone" : "Another",
            pageUrl: ModLink(id),
            updatedAt: January.AddDays(number),
            isAvailable: number != 4,
            hasAdultContent: number == 3,
            downloads: 1000 * number,
            endorsements: 10 * number);
    }

    private static ModFileRef File(string mod, string file)
        => new(new ModRef(new GameRef(SourceId, "game"), mod), file);

    private static ModFile Listed(ModRef mod, string id, string name, string? version, ModFileCategory category, int days)
        => new(new ModFileRef(mod, id), name, $"file-{id}.zip", version, category, 1024 * 1024, January.AddDays(days), [], FileLink(mod.Id, id));

    private static void Fail(string mod)
    {
        switch (mod)
        {
            case "404":
                throw new HttpRequestException("No Mod Found", null, HttpStatusCode.NotFound);
            case "401":
                throw new AuthenticationFailedException("The site wants an account first.");
            default:
                break;
        }
    }
}
