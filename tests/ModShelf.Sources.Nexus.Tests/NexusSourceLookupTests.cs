using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Lookup;
using ModShelf.Sources.Nexus.Tests.Common;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// A game's files found by their MD5: one, none, and the same archive uploaded to more than one mod.
/// </summary>
/// <remarks>
/// The answers are written by hand, to the shape Nexus Mods' own client reads (node-nexus-api's <c>IMD5Result</c>):
/// recording them would need archives of real mods.
/// </remarks>
public sealed class NexusSourceLookupTests : IDisposable
{
    private const string Md5 = "0123456789abcdef0123456789abcdef";
    private const string SearchPath = $"/v1/games/stardewvalley/mods/md5_search/{Md5}.json";

    private static readonly GameRef StardewValley = new(new SourceId("nexus"), "stardewvalley");
    private static readonly FileHash Hash = new(FileHashAlgorithm.Md5, Md5.ToUpperInvariant());

    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceLookupTests()
    {
        _services = _api.BuildServices();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private NexusSource Source => _services.GetServices<IModSource>().OfType<NexusSource>().Single();

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    [Fact]
    public async Task FindsTheFileWithItsModAndItsMd5()
    {
        _api.Answer(SearchPath, response => response.WithStatusCode(200).WithBody(
            $$"""
            [
              {
                "mod": {"mod_id": 2400, "domain_name": "stardewvalley", "name": "SMAPI - Stardew Modding API", "author": "Pathoschild",
                        "summary": "The mod loader.", "version": "4.5.2", "available": true, "status": "published"},
                "file_details": {"file_id": 140005, "name": "SMAPI 4.5.2", "version": "4.5.2", "category_id": 1, "category_name": "MAIN",
                        "file_name": "SMAPI 4.5.2-2400-4-5-2-1760000000.zip", "uploaded_timestamp": 1760000000,
                        "size_in_bytes": 1048576, "md5": "{{Md5}}"}
              }
            ]
            """));

        IReadOnlyList<FoundFile> found = await Source.FindByHashAsync(StardewValley, Hash, Cancellation);

        FoundFile file = Assert.Single(found);
        Assert.Equal((new ModRef(StardewValley, "2400"), "SMAPI - Stardew Modding API", "Pathoschild"), (file.Mod.Mod, file.Mod.Name, file.Mod.Author));
        Assert.Equal(
            (new ModFileRef(new ModRef(StardewValley, "2400"), "140005"), "SMAPI 4.5.2", "SMAPI 4.5.2-2400-4-5-2-1760000000.zip", "4.5.2", ModFileCategory.Main, 1048576L),
            (file.File.Reference, file.File.Name, file.File.FileName, file.File.VersionLabel, file.File.Category, file.File.SizeInBytes));
        Assert.Equal([new FileHash(FileHashAlgorithm.Md5, Md5)], file.File.Hashes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760000000), file.File.UploadedAt);
        Assert.Equal(["/v1/games/stardewvalley/mods/md5_search/0123456789abcdef0123456789abcdef.json"], _api.Requests.Select(request => request.RequestMessage?.Path));
    }

    [Fact]
    public async Task AnMd5ItDoesntKnowIsNoFile()
    {
        _api.Answer(SearchPath, response => response.WithStatusCode(404).WithBody("""{"code": 404, "message": "No File found."}"""));

        Assert.Empty(await Source.FindByHashAsync(StardewValley, Hash, Cancellation));
    }

    [Fact]
    public async Task ListsEachFileOfTheGameOnceLeavingOutWhatItCantRead()
    {
        _api.Answer(SearchPath, response => response.WithStatusCode(200).WithBody(
            """
            [
              {"mod": {"mod_id": 2400, "domain_name": "stardewvalley", "name": "SMAPI"},
               "file_details": {"file_id": 140005, "name": "SMAPI", "file_name": "smapi.zip", "category_id": 1, "uploaded_timestamp": 1760000000}},
              {"mod": {"mod_id": 9999, "domain_name": "stardewvalley", "name": "SMAPI reupload"},
               "file_details": {"file_id": 150000, "name": "SMAPI", "file_name": "smapi.zip", "category_id": 7, "uploaded_timestamp": 1760000100}},
              {"mod": {"mod_id": 2400, "domain_name": "StardewValley", "name": "SMAPI"},
               "file_details": {"file_id": 140005, "name": "SMAPI", "file_name": "smapi.zip", "category_id": 1, "uploaded_timestamp": 1760000000}},
              {"mod": {"mod_id": 77, "domain_name": "skyrimspecialedition", "name": "Elsewhere"},
               "file_details": {"file_id": 1, "name": "Elsewhere", "file_name": "e.zip", "category_id": 1, "uploaded_timestamp": 1}},
              {"mod": {"mod_id": 0, "domain_name": "stardewvalley", "name": "No ID"},
               "file_details": {"file_id": 2, "name": "No ID", "file_name": "n.zip", "category_id": 1, "uploaded_timestamp": 1}},
              {"mod": {"mod_id": 5, "name": "No file"}},
              null
            ]
            """));

        IReadOnlyList<FoundFile> found = await Source.FindByHashAsync(StardewValley, Hash, Cancellation);

        Assert.Equal(["2400/140005", "9999/150000"], found.Select(file => $"{file.File.Reference.Mod.Id}/{file.File.Reference.Id}"));
        Assert.Equal([new FileHash(FileHashAlgorithm.Md5, Md5)], found[1].File.Hashes);
        Assert.Equal(ModFileCategory.Unknown, found[1].File.Category);
    }

    [Fact]
    public async Task ARefusedAccountIsSaidSo()
    {
        _api.Answer(SearchPath, response => response.WithStatusCode(401).WithBody("""{"message": "Please provide a valid API Key"}"""));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => Source.FindByHashAsync(StardewValley, Hash, Cancellation));
    }

    [Fact]
    public async Task RefusesAnotherSourcesGameOrAnotherHashWithoutAskingTheApi()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Source.FindByHashAsync(new GameRef(new SourceId("other"), "stardewvalley"), Hash, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Source.FindByHashAsync(StardewValley, new FileHash(FileHashAlgorithm.Sha256, new string('a', 64)), Cancellation));

        Assert.Empty(_api.Requests);
        Assert.Equal(FileHashAlgorithm.Md5, Source.LookupAlgorithm);
    }
}
