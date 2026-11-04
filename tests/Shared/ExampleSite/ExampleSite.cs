using System.Net;
using System.Security.Cryptography;
using System.Text;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Testing;

/// <summary>
/// A mod site unlike Nexus Mods, which the tests run through the app, so every layer is proven with two sites
/// (<c>docs/adding-a-source.md</c>). Its games' keys have hyphens, and its mods' IDs are letters; it lists each version
/// of a mod as a main file, with no update chains, exact sizes and SHA-512 hashes; its files download directly, with
/// no account and no handoff; its search has only some of the orders, and refuses the rest. It has no update feed,
/// watchlist, trending mods, requirements, quota, changelogs or pictures.
/// </summary>
/// <remarks>
/// <para>
/// Its links are <c>https://mods.example.org/{game}/mod/{mod}</c>, for the mod's newest file, and
/// <c>…/mod/{mod}/file/{file}</c>. A game or mod it doesn't have is a 404, as a site's would be.
/// </para>
/// <para>
/// Its games are Stardew Valley, <c>stardew-valley</c>, which Nexus Mods has too, and Pixel Farm, <c>pixel-farm</c>, with
/// 55 mods to page through. Better Crops (<c>k7Qx2p</c>) has versions 1.0 and 1.1; the test can add more with
/// <see cref="AddVersion"/>.
/// </para>
/// </remarks>
public sealed partial class ExampleSite : ILinkRecognizer, IDownloadResolver, IModCatalog, IModSearch
{
    public const string Host = "mods.example.org";

    public const string BetterCrops = "k7Qx2p";

    public const string BetterCrops10 = "v1a2b";

    public const string BetterCrops11 = "v3c4d";

    public const string Hoes = "Hq9wE2";

    /// <summary>
    /// The most mods a page of its search holds, whatever the page size asked for.
    /// </summary>
    public const int PageLimit = 50;

    /// <summary>
    /// When its first file was uploaded; the rest follow, a day or more apart.
    /// </summary>
    public static readonly DateTimeOffset Opened = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private readonly Dictionary<ModRef, ExampleMod> _mods = [];
    private readonly Uri? _downloadUrl;
    private readonly byte[]? _content;

    /// <param name="downloadUrl">Where every file downloads from, such as a test's file server; by default, a place of its own.</param>
    /// <param name="content">What every file holds, for its size and hash; by default, each file's name.</param>
    public ExampleSite(Uri? downloadUrl = null, byte[]? content = null)
    {
        _downloadUrl = downloadUrl;
        _content = content;

        ExampleMod crops = Add(StardewValley.Game, BetterCrops, "Better Crops", "Grows crops a little faster.", "Ana", 9120);
        crops.Files.Add(Version(crops, BetterCrops10, "1.0", Opened));
        crops.Files.Add(Version(crops, BetterCrops11, "1.1", Opened.AddDays(10)));

        ExampleMod hoes = Add(StardewValley.Game, Hoes, "Golden Hoes", "Hoes that never wear out.", "Bo", 310);
        hoes.Files.Add(Version(hoes, "h5k8m", "2.0", Opened.AddDays(3)));
        hoes.Files.Add(new ModFile(
            new ModFileRef(hoes.Mod, "h5k8x"),
            "Golden Hoes sprites",
            "golden-hoes-sprites.zip",
            null,
            ModFileCategory.Optional,
            SizeOf("golden-hoes-sprites.zip"),
            Opened.AddDays(4),
            [HashOf("golden-hoes-sprites.zip")],
            FileLink(hoes.Mod, "h5k8x")));

        for (int number = 1; number <= 55; number++)
        {
            ExampleMod tool = Add(PixelFarm.Game, $"pt{Letters(number)}", $"Pixel Tool {number}", $"Tool number {number}.", "Cy", number * 10);
            tool.Files.Add(Version(tool, $"pf{Letters(number)}", "1.0", Opened.AddDays(number % 20)));
        }
    }

    public static SourceId SourceId { get; } = new("example");

    public static GameInfo StardewValley { get; } = new(
        new GameRef(SourceId, "stardew-valley"), "Stardew Valley", new Uri($"https://{Host}/stardew-valley"));

    public static GameInfo PixelFarm { get; } = new(
        new GameRef(SourceId, "pixel-farm"), "Pixel Farm", new Uri($"https://{Host}/pixel-farm"));

    public SourceId Id => SourceId;

    public string DisplayName => "Example Mods";

    public static Uri ModLink(ModRef mod) => new($"https://{Host}/{mod.Game.Key}/mod/{mod.Id}");

    public static Uri FileLink(ModRef mod, string file) => new($"https://{Host}/{mod.Game.Key}/mod/{mod.Id}/file/{file}");

    public static ModRef ModOf(GameInfo game, string id) => new(game.Game, id);

    /// <summary>
    /// Adds a version of a mod: a main file, as the site lists every version.
    /// </summary>
    public ModFile AddVersion(ModRef mod, string file, string version, DateTimeOffset uploadedAt)
    {
        ArgumentNullException.ThrowIfNull(mod);
        lock (_gate)
        {
            ExampleMod listed = _mods[mod];
            ModFile added = Version(listed, file, version, uploadedAt);
            listed.Files.Add(added);
            return added;
        }
    }

    public Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ModFile listed = FileOf(file);
        Uri from = _downloadUrl ?? new Uri($"https://files.{Host}/{file.Id}/{listed.FileName}");
        return Task.FromResult<ResolveResult>(new DownloadTicket(from, expectedHash: listed.Hashes[0]));
    }

    private static string Letters(int number) => $"{(char)('a' + (number / 26))}{(char)('a' + (number % 26))}";

    private static HttpRequestException NotFound(string what) => new($"Example Mods has no {what}.", null, HttpStatusCode.NotFound);

    private static void EnsureOwn(GameRef game, string argument)
    {
        if (game.Source != SourceId)
        {
            throw new ArgumentException($"The game belongs to source '{game.Source}', not '{SourceId}'.", argument);
        }
    }

    private ExampleMod Add(GameRef game, string id, string name, string summary, string author, long downloads)
    {
        ExampleMod mod = new(new ModRef(game, id), name, summary, author, downloads);
        _mods.Add(mod.Mod, mod);
        return mod;
    }

    private ModFile Version(ExampleMod mod, string file, string version, DateTimeOffset uploadedAt)
    {
        string fileName = $"{mod.Name.ToLowerInvariant().Replace(' ', '-')}-{version}.zip";
        return new ModFile(
            new ModFileRef(mod.Mod, file),
            $"{mod.Name} {version}",
            fileName,
            version,
            ModFileCategory.Main,
            SizeOf(fileName),
            uploadedAt,
            [HashOf(fileName)],
            FileLink(mod.Mod, file));
    }

    private long SizeOf(string fileName) => _content?.LongLength ?? Encoding.UTF8.GetByteCount(fileName);

    private FileHash HashOf(string fileName)
        => new(FileHashAlgorithm.Sha512, Convert.ToHexStringLower(SHA512.HashData(_content ?? Encoding.UTF8.GetBytes(fileName))));

    private ExampleMod ModNamed(ModRef mod)
    {
        lock (_gate)
        {
            return _mods.TryGetValue(mod, out ExampleMod? listed) ? listed : throw NotFound($"mod {mod.Id} of {mod.Game.Key}");
        }
    }

    private ModFile FileOf(ModFileRef file)
    {
        EnsureOwn(file.Mod.Game, nameof(file));
        ExampleMod mod = ModNamed(file.Mod);
        lock (_gate)
        {
            return mod.Files.FirstOrDefault(listed => listed.Reference == file) ?? throw NotFound($"file {file.Id} of mod {file.Mod.Id}");
        }
    }
}
