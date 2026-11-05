using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Diagnostics;
using ModShelf.Application.Diagnostics.Ports;
using ModShelf.Infrastructure.Diagnostics;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Diagnostics;

/// <summary>
/// The count of deprecated calls in deprecated-calls.json: counted as documented, by several at once without losing one,
/// a file that can't be read forgotten and started again, a count that isn't what it should be started again, and what
/// else the file holds kept.
/// </summary>
public sealed class JsonDeprecatedCallStoreTests : IDisposable
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 9, 12, 0, TimeSpan.Zero);

    private readonly TempFolder _folder = new();
    private readonly RecordingLogger<JsonDeprecatedCallStore> _logger = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_folder.Path, "App Data", "deprecated-calls.json");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task WithoutAFileNothingIsCounted()
    {
        Assert.Empty(await Store().LoadAsync(Cancellation));
        Assert.Equal(FilePath, Store().Location);
        Assert.False(File.Exists(FilePath));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task CountsEachCallAsTheDocumentedJson()
    {
        await Store().AddAsync("download.link", "DL_LINK_ARG", "Claude Code", Monday, Cancellation);
        await Store().AddAsync("download.link", "DL_LINK_ARG", "claude-ai", new DateTimeOffset(2026, 11, 3, 20, 0, 0, TimeSpan.Zero), Cancellation);

        // Written in UTC, whatever the time's offset.
        await Store().AddAsync("download.link", "DL_LINK_ARG", "claude-ai", new DateTimeOffset(2026, 11, 3, 23, 31, 0, TimeSpan.FromHours(1)), Cancellation);

        Assert.Equal(
            [new DeprecatedCall("download.link", "DL_LINK_ARG", 3, Monday, new DateTimeOffset(2026, 11, 3, 22, 31, 0, TimeSpan.Zero), "claude-ai")],
            await Store().LoadAsync(Cancellation));
        string expected = """
            {
              "download.link": {
                "code": "DL_LINK_ARG",
                "count": 3,
                "first": "2026-11-02T09:12:00Z",
                "last": "2026-11-03T22:31:00Z",
                "client": "claude-ai"
              }
            }
            """;
        Assert.Equal(expected.ReplaceLineEndings() + Environment.NewLine, await File.ReadAllTextAsync(FilePath, Cancellation));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task ACallWithoutAClientSaysNone()
    {
        await Store().AddAsync("download.link", "DL_LINK_ARG", "claude-ai", Monday, Cancellation);
        await Store().AddAsync("download.link", "DL_LINK_ARG", null, Monday.AddHours(1), Cancellation);

        Assert.Null(Assert.Single(await Store().LoadAsync(Cancellation)).Client);
        Assert.Contains("\"client\": null", await File.ReadAllTextAsync(FilePath, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListsTheCallsByNameAndKeepsWhatElseTheFileHolds()
    {
        await WriteAsync("""
            {
              // Written by hand.
              "note": "kept",
              "zebra.old": { "code": "Z_OLD_ARG", "count": 2, "first": "2026-11-01T00:00:00+02:00", "last": "2026-11-01T10:00:00Z", "client": "zed", "extra": true },
            }
            """);

        await Store().AddAsync("download.link", "DL_LINK_ARG", null, Monday, Cancellation);

        Assert.Equal(
            [
                new DeprecatedCall("download.link", "DL_LINK_ARG", 1, Monday, Monday, null),
                new DeprecatedCall("zebra.old", "Z_OLD_ARG", 2, new DateTimeOffset(2026, 10, 31, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), "zed"),
            ],
            await Store().LoadAsync(Cancellation));
        string written = await File.ReadAllTextAsync(FilePath, Cancellation);
        Assert.Contains("\"note\": \"kept\"", written, StringComparison.Ordinal);
        Assert.Contains("\"extra\": true", written, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("[ 1, 2 ]")]
    [InlineData("\"download.link\"")]
    public async Task AFileThatCantBeReadIsForgottenAndStartedAgain(string content)
    {
        await WriteAsync(content);

        IReadOnlyList<DeprecatedCall> before = await Store().LoadAsync(Cancellation);
        await Store().AddAsync("download.link", "DL_LINK_ARG", null, Monday, Cancellation);

        Assert.Empty(before);
        Assert.Contains(_logger.Entries, entry => entry.Contains(FilePath, StringComparison.Ordinal) && entry.Contains("can't be read", StringComparison.Ordinal));
        Assert.Equal([new DeprecatedCall("download.link", "DL_LINK_ARG", 1, Monday, Monday, null)], await Store().LoadAsync(Cancellation));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n ")]
    public async Task AnEmptyFileHasNothingCountedYet(string content)
    {
        await WriteAsync(content);

        Assert.Empty(await Store().LoadAsync(Cancellation));
        await Store().AddAsync("download.link", "DL_LINK_ARG", null, Monday, Cancellation);

        Assert.Equal(1, Assert.Single(await Store().LoadAsync(Cancellation)).Count);
        Assert.Empty(_logger.Entries);
    }

    [Theory]
    [InlineData("""{ "code": "DL_LINK_ARG", "count": 0, "first": "2026-11-01T00:00:00Z", "last": "2026-11-01T00:00:00Z" }""")]
    [InlineData("""{ "code": "DL_LINK_ARG", "count": "3", "first": "2026-11-01T00:00:00Z", "last": "2026-11-01T00:00:00Z" }""")]
    [InlineData("""{ "code": "DL_LINK_ARG", "count": 1.5, "first": "2026-11-01T00:00:00Z", "last": "2026-11-01T00:00:00Z" }""")]
    [InlineData("""{ "code": "DL_LINK_ARG", "first": "2026-11-01T00:00:00Z", "last": "2026-11-01T00:00:00Z" }""")]
    [InlineData("""{ "code": " ", "count": 3, "first": "2026-11-01T00:00:00Z", "last": "2026-11-01T00:00:00Z" }""")]
    [InlineData("""{ "code": 3, "count": 3, "first": "2026-11-01T00:00:00Z", "last": "2026-11-01T00:00:00Z" }""")]
    [InlineData("""{ "count": 3, "first": "2026-11-01T00:00:00Z", "last": "2026-11-01T00:00:00Z" }""")]
    [InlineData("""{ "code": "DL_LINK_ARG", "count": 3, "first": "yesterday", "last": "2026-11-01T00:00:00Z" }""")]
    [InlineData("""{ "code": "DL_LINK_ARG", "count": 3, "last": "2026-11-01T00:00:00Z" }""")]
    [InlineData("""{ "code": "DL_LINK_ARG", "count": 3, "first": "2026-11-01T00:00:00Z", "last": 20261101 }""")]
    [InlineData("""{ "code": "DL_LINK_ARG", "count": 3, "first": "2026-11-01T00:00:00Z" }""")]
    [InlineData("""{ "code": "DL_LINK_ARG", "count": 3, "first": "2026-11-01T00:00:00Z", "last": "2026-11-01T00:00:00Z", "client": 7 }""")]
    [InlineData("""[ "DL_LINK_ARG", 3 ]""")]
    public async Task ACountThatIsntWhatItShouldBeIsLeftOutAndStartsAgain(string entry)
    {
        await WriteAsync($$"""{ "download.link": {{entry}}, "other.old": { "code": "O_OLD_ARG", "count": 4, "first": "2026-11-01T00:00:00Z", "last": "2026-11-01T00:00:00Z" } }""");

        IReadOnlyList<DeprecatedCall> before = await Store().LoadAsync(Cancellation);
        await Store().AddAsync("download.link", "DL_LINK_ARG", null, Monday, Cancellation);

        Assert.Equal(["other.old"], before.Select(call => call.Name));
        Assert.Equal(
            [new DeprecatedCall("download.link", "DL_LINK_ARG", 1, Monday, Monday, null), new DeprecatedCall("other.old", "O_OLD_ARG", 4, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), null)],
            await Store().LoadAsync(Cancellation));
    }

    [Fact]
    public async Task ATimeWithoutAnOffsetIsUtc()
    {
        await WriteAsync("""{ "download.link": { "code": "DL_LINK_ARG", "count": 1, "first": "2026-11-01T08:00:00", "last": "2026-11-01T09:00:00" } }""");

        DeprecatedCall call = Assert.Single(await Store().LoadAsync(Cancellation));

        Assert.Equal((new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero)), (call.First, call.Last));
    }

    [Fact]
    public async Task TheCountStopsAtTheLargestNumber()
    {
        await WriteAsync($$"""{ "download.link": { "code": "DL_LINK_ARG", "count": {{long.MaxValue}}, "first": "2026-11-01T00:00:00Z", "last": "2026-11-01T00:00:00Z" } }""");

        await Store().AddAsync("download.link", "DL_LINK_ARG", null, Monday, Cancellation);

        Assert.Equal(long.MaxValue, Assert.Single(await Store().LoadAsync(Cancellation)).Count);
    }

    [Fact]
    public async Task ManyCountingAtOnceLoseNoCall()
    {
        JsonDeprecatedCallStore[] stores = [Store(), Store(), Store()];

        await Task.WhenAll(Enumerable.Range(0, 30).Select(index => Task.Run(
            () => stores[index % stores.Length].AddAsync(index % 2 == 0 ? "download.link" : "other.old", "CODE", null, Monday.AddMinutes(index), Cancellation),
            Cancellation)));

        IReadOnlyList<DeprecatedCall> counted = await Store().LoadAsync(Cancellation);
        Assert.Equal([("download.link", 15L), ("other.old", 15L)], counted.Select(call => (call.Name, call.Count)));
    }

    [Fact]
    public async Task WaitsWhileAnotherHasTheFileOpen()
    {
        await Store().AddAsync("download.link", "DL_LINK_ARG", null, Monday, Cancellation);
        Task counting;
        Task<IReadOnlyList<DeprecatedCall>> reading;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            counting = Store().AddAsync("download.link", "DL_LINK_ARG", null, Monday.AddHours(1), Cancellation);
            reading = Store().LoadAsync(Cancellation);
            await Task.Delay(150, Cancellation);
            Assert.False(counting.IsCompleted);
            Assert.False(reading.IsCompleted);
        }

        await counting;

        // Read before or after the count: either way, whole.
        Assert.Contains(Assert.Single(await reading).Count, new long[] { 1, 2 });
        Assert.Equal(2, Assert.Single(await Store().LoadAsync(Cancellation)).Count);
    }

    [Fact]
    public async Task GivesUpAfterAWhileSayingWhyAndCountsNothing()
    {
        await Store().AddAsync("download.link", "DL_LINK_ARG", null, Monday, Cancellation);
        IOException refused;
        IReadOnlyList<DeprecatedCall> read;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // A store that never gives up fails the test rather than hanging it.
            refused = await Assert.ThrowsAsync<IOException>(() => Store().AddAsync("download.link", "DL_LINK_ARG", null, Monday.AddHours(1), Cancellation).WaitAsync(TimeSpan.FromSeconds(30), Cancellation));
            read = await Store().LoadAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        }

        Assert.StartsWith($"The deprecated call couldn't be counted in {FilePath}: ", refused.Message, StringComparison.Ordinal);
        Assert.Empty(read);
        Assert.Contains(_logger.Entries, entry => entry.Contains("can't be read", StringComparison.Ordinal));
        Assert.Equal(1, Assert.Single(await Store().LoadAsync(Cancellation)).Count);
    }

    [Fact]
    public async Task AFolderInTheFilesPlaceIsForgottenAndCountingSaysWhy()
    {
        Directory.CreateDirectory(FilePath);

        IReadOnlyList<DeprecatedCall> read = await Store().LoadAsync(Cancellation);
        IOException refused = await Assert.ThrowsAsync<IOException>(() => Store().AddAsync("download.link", "DL_LINK_ARG", null, Monday, Cancellation));

        Assert.Empty(read);
        Assert.StartsWith($"The deprecated call couldn't be counted in {FilePath}: ", refused.Message, StringComparison.Ordinal);
        Assert.NotEmpty(_logger.Entries);
    }

    [Theory]
    [InlineData(" ", "DL_LINK_ARG")]
    [InlineData("download.link", "")]
    public async Task ABlankNameOrCodeIsRefused(string name, string code)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store().AddAsync(name, code, null, Monday, Cancellation));

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void RefusesARelativePath()
    {
        Assert.Throws<ArgumentException>(() => new JsonDeprecatedCallStore(Path.Combine("App Data", "deprecated-calls.json"), _logger));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddDeprecatedCallsFile("deprecated-calls.json"));
    }

    [Fact]
    public async Task IsRegisteredAsTheDeprecatedCallStore()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDeprecatedCallsFile(FilePath);
        await using ServiceProvider provider = services.BuildServiceProvider();

        IDeprecatedCallStore store = provider.GetRequiredService<IDeprecatedCallStore>();
        await store.AddAsync("download.link", "DL_LINK_ARG", null, Monday, Cancellation);

        Assert.Equal(FilePath, store.Location);
        Assert.Single(await store.LoadAsync(Cancellation));
    }

    [Fact]
    public void TheLibrarysFilesKeepItBesideTheSettings()
    {
        ServiceCollection services = new();
        services.AddLogging();
        string database = Path.Combine(_folder.Path, "Data", "library.db");

        string folder = services.AddLibraryFiles(database, "app");

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Equal(Path.Combine(folder, "deprecated-calls.json"), provider.GetRequiredService<IDeprecatedCallStore>().Location);
    }

    private JsonDeprecatedCallStore Store() => new(FilePath, _logger);

    private async Task WriteAsync(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, content, Cancellation);
    }
}
