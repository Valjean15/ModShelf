using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.CurseForge.Api;
using ModShelf.Sources.CurseForge.Tests.Common;

namespace ModShelf.Sources.CurseForge.Tests;

/// <summary>
/// What the CurseForge source makes of CurseForge's answers as recorded (ADR 0107).
/// </summary>
public sealed class CurseForgeSourceRecordedTests : IDisposable
{
    private static readonly SourceId CurseForge = new("curseforge");

    private readonly CurseForgeServer _server = new();
    private ServiceProvider? _services;

    public CurseForgeSourceRecordedTests()
    {
        _server.ServeRecordings();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static JsonNode Mod => CurseForgeServer.Body("mod")["data"]!;

    private static GameRef Game => new(CurseForge, CurseForgeSourceTests.ModPage.Segments[1].TrimEnd('/'));

    private static ModRef ModRef => new(Game, Mod["id"]!.GetValue<long>().ToString(CultureInfo.InvariantCulture));

    private CurseForgeSource Source => CurseForgeServer.SourceOf(_services ??= _server.BuildServices());

    public void Dispose()
    {
        _services?.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task DescribesTheModAndItsGameAsRecorded()
    {
        ModInfo mod = await Source.GetModAsync(ModRef, Cancellation);
        GameInfo game = await Source.GetGameAsync(Game, Cancellation);

        Assert.Equal(Mod["name"]!.GetValue<string>().Trim(), mod.Name);
        Assert.Equal(CurseForgeSourceTests.ModPage.AbsoluteUri.TrimEnd('/'), mod.PageUrl!.AbsoluteUri.TrimEnd('/'));
        Assert.Contains(ClassOfMod(), game.Categories);
    }

    [Fact]
    public async Task ListsEveryRecordedFileEachUpdatedOnlyByANewerBuildForTheSameGameVersions()
    {
        JsonNode[] recorded = [.. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Recorded"), "files-*.json")
            .SelectMany(file => CurseForgeServer.Body(Path.GetFileNameWithoutExtension(file))["data"]!.AsArray()!)
            .Select(file => file!)];

        IReadOnlyList<ModFile> files = await Source.GetFilesAsync(ModRef, Cancellation);

        Assert.Equal(recorded.Select(file => file["id"]!.GetValue<long>()).Distinct().Count(), files.Count);
        Assert.Contains(files, file => file.Reference.Id == CurseForgeSourceTests.MainFile);
        Assert.All(files.Where(file => file.ReplacedBy is not null), file =>
        {
            ModFile newer = files.Single(listed => listed.Reference == file.ReplacedBy);
            Assert.True(newer.UploadedAt >= file.UploadedAt);
            Assert.NotEqual(ModFileCategory.Archived, newer.Category);
        });
        Assert.All(files, file => Assert.Contains(file.Hashes, hash => hash.Algorithm == FileHashAlgorithm.Sha1));
    }

    [Fact]
    public async Task NamesWhatTheMainFileNeeds()
    {
        JsonNode[] named = [.. CurseForgeServer.Body("mods")["data"]!.AsArray().Select(mod => mod!)];

        IReadOnlyList<ModRequirement> needed = await Source.GetRequirementsAsync(ModRef, Cancellation);

        Assert.Equal(
            named.Select(mod => (mod["name"]!.GetValue<string>().Trim(), mod["id"]!.GetValue<long>().ToString(CultureInfo.InvariantCulture))).Order(),
            needed.Select(requirement => (requirement.Name, requirement.Mod!.Id)).Order());
        Assert.All(needed, requirement => Assert.Equal(Game, requirement.Mod!.Game));
    }

    [Fact]
    public async Task SearchesTheModsClassAsRecorded()
    {
        string query = CurseForgeServer.Recording("search-2")["request"]!["query"]!.GetValue<string>();
        string words = Uri.UnescapeDataString(query.Split('&').Single(pair => pair.StartsWith("searchFilter=", StringComparison.Ordinal))["searchFilter=".Length..]);
        JsonNode body = CurseForgeServer.Body("search-2");

        SearchPage page = await Source.SearchAsync(Game, new SearchQuery(words, SearchSort.Relevance, offset: 50, count: 50, category: ClassOfMod()), Cancellation);

        Assert.Equal(body["data"]!.AsArray().Select(mod => mod!["id"]!.GetValue<long>().ToString(CultureInfo.InvariantCulture)), page.Mods.Select(mod => mod.Mod.Id));
        Assert.Equal(Math.Min(body["pagination"]!["totalCount"]!.GetValue<long>(), 10_000), page.TotalCount);
        Assert.All(page.Mods, mod => Assert.Equal((Game, ClassOfMod()), (mod.Mod.Game, mod.Category)));
        Assert.Contains("GET /v1/mods/search?" + query, _server.Asked);
    }

    [Fact]
    public async Task AModThatDoesntExistIsntFound()
    {
        HttpRequestException missing = await Assert.ThrowsAsync<HttpRequestException>(() => Source.GetModAsync(new ModRef(Game, "1"), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("no-key")]
    [InlineData("wrong-key")]
    public async Task ARequestWithoutAKeyCurseForgeTakesIsTheAccountToLinkAgain(string recording)
    {
        _services = _server.BuildServices(key: CurseForgeServer.KeyOf(recording));
        JsonNode request = CurseForgeServer.Recording(recording)["request"]!;
        string query = request["query"]!.GetValue<string>();
        string target = request["path"]!.GetValue<string>().TrimStart('/') + (query.Length > 0 ? "?" + query : string.Empty);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => _services.GetRequiredService<CurseForgeApiClient>().GetAsync<JsonNode>(target, Cancellation));
    }

    private static string ClassOfMod()
    {
        long classId = Mod["classId"]!.GetValue<long>();
        return CurseForgeServer.Body("classes")["data"]!.AsArray().Single(type => type!["id"]!.GetValue<long>() == classId)!["name"]!.GetValue<string>().Trim();
    }
}
