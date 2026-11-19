using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A site that lists what its mods need, at <c>https://requiring.example/game/mods/{mod}[/files/{file}]</c>, and lets
/// every file be downloaded straight away, from <paramref name="fileUrl"/>. Mod <c>main</c> needs mod <c>lib</c>, a
/// script extender off the site and mod <c>bare</c>; <c>lib</c> needs <c>core</c>, which needs nothing. Each mod has one
/// main file, 1, named <c>{mod}-1.zip</c>, but <c>bare</c>, which has only an optional one. A link to a mod is to its
/// file 1.
/// </summary>
internal sealed class FakeRequiringSource(Uri fileUrl) : ILinkRecognizer, IDownloadResolver, IModCatalog, IModRequirements
{
    public const string ExtenderPage = "https://extender.example/";

    private const string Host = "requiring.example";

    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["main"] = "Main Mod",
        ["lib"] = "Lib Mod",
        ["core"] = "Core Mod",
        ["bare"] = "Bare Mod",
    };

    public static SourceId SourceId { get; } = new("fake-requiring");

    public SourceId Id => SourceId;

    public string DisplayName => "Fake requiring site";

    public SiteWords Words => SiteWords.None;

    public bool HandsOverLinks => true;

    public static Uri ModLink(string mod) => new($"https://{Host}/game/mods/{mod}");

    public static Uri FileLink(string mod, string file = "1") => new($"https://{Host}/game/mods/{mod}/files/{file}");

    public static string FileNameOf(string mod, string file = "1") => $"{mod}-{file}.zip";

    public static string ModName(string mod) => Names[mod];

    public Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.Host != Host)
        {
            return Task.FromResult<RecognizedLink?>(null);
        }

        string[] path = link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string mod = path[2];
        string file = path.Length > 4 ? path[4] : "1";
        return Task.FromResult<RecognizedLink?>(new RecognizedLink(File(mod, file), FileNameOf(mod, file)));
    }

    public Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
        => Task.FromResult<ResolveResult>(new DownloadTicket(fileUrl));

    public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<GameInfo>>([new GameInfo(new GameRef(Id, "game"), "Requiring Game")]);

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
        => Task.FromResult(new GameInfo(game, "Requiring Game"));

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        return Task.FromResult(new ModInfo(modRef, Names[modRef.Id], author: "Someone", pageUrl: ModLink(modRef.Id)));
    }

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        ModFileCategory category = modRef.Id == "bare" ? ModFileCategory.Optional : ModFileCategory.Main;
        return Task.FromResult<IReadOnlyList<ModFile>>(
        [
            new ModFile(
                File(modRef.Id, "1"),
                $"{Names[modRef.Id]} 1.0",
                FileNameOf(modRef.Id),
                "1.0",
                category,
                null,
                DateTimeOffset.UnixEpoch,
                [],
                FileLink(modRef.Id)),
        ]);
    }

    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ModChangelog>>([]);

    public Task<IReadOnlyList<ModRequirement>> GetRequirementsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        return Task.FromResult<IReadOnlyList<ModRequirement>>(modRef.Id switch
        {
            "main" =>
            [
                Needed("lib"),
                new ModRequirement("Script Extender", pageUrl: new Uri(ExtenderPage), notes: "Get the build for your game's version."),
                Needed("bare"),
            ],
            "lib" => [Needed("core")],
            _ => [],
        });
    }

    private static ModRequirement Needed(string mod) => new(Names[mod], Mod(mod), ModLink(mod));

    private static ModRef Mod(string mod) => new(new GameRef(SourceId, "game"), mod);

    private static ModFileRef File(string mod, string file) => new(Mod(mod), file);
}
