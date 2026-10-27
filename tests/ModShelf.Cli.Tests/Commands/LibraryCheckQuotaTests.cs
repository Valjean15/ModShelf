using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Http;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// What Phase 4 is done for: a library of 200 watched mods across 5 games is checked for updates within Nexus Mods' rate
/// limit, and moving it leaves every record valid. The real Nexus Mods source answers from <see cref="FakeNexusApi"/>,
/// and the library has one file of each mod, on disk.
/// </summary>
public sealed class LibraryCheckQuotaTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;

    private const int ModsPerGame = 40;

    private static readonly string[] Games = ["skyrimspecialedition", "fallout4", "stardewvalley", "cyberpunk2077", "baldursgate3"];
    private static readonly byte[] Content = "mod!"u8.ToArray();

    // To the second, as the API gives times.
    private static readonly DateTimeOffset Uploaded = DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().AddDays(-100).ToUnixTimeSeconds());

    private readonly FakeNexusApi _api = new();
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly InProcessCli _cli;

    public LibraryCheckQuotaTests()
    {
        _cli = new InProcessCli(
            _site,
            _browser,
            configureServices: services => services
                .AddHttpClient(SourceHttpClients.NameFor(new SourceId("nexus")))
                .ConfigureHttpClient(http => http.BaseAddress = _api.BaseAddress));
        foreach (string game in Games)
        {
            for (int number = 1; number <= ModsPerGame; number++)
            {
                _api.AddMod(game, ModId(game, number), Uploaded);
            }
        }
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_cli.Downloads, "ModShelf");

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task ChecksTwoHundredModsInFiveGamesWithinTheRateLimit()
    {
        await LinkAndFillTheLibraryAsync();

        CliResult first = await RunAsync("library", "check");
        IReadOnlyList<string> firstRequests = _api.Requests;
        _api.ForgetRequests();
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        _api.PublishUpdate(Games[0], ModId(Games[0], 7), now);
        _api.PublishUpdate(Games[0], ModId(Games[0], 21), now);
        _api.PublishUpdate(Games[3], ModId(Games[3], 40), now);
        CliResult second = await RunAsync("library", "check");

        Assert.Equal(Done, first.ExitCode);
        Assert.Equal(["Checked 200 mods with 200 requests.", "No new updates."], Lines(first));
        Assert.Equal(200, firstRequests.Count);
        Assert.All(firstRequests, path => Assert.EndsWith("/files.json", path, StringComparison.Ordinal));
        Assert.Equal(Done, second.ExitCode);
        Assert.Equal(["Checked 200 mods with 8 requests.", "3 new updates:"], Lines(second)[..2]);
        Assert.Equal(
            [.. Games.Select(game => $"/v1/games/{game}/mods/updated.json")],
            [.. _api.Requests.Where(path => path.EndsWith("/updated.json", StringComparison.Ordinal))]);
        Assert.Equal(
            [$"/v1/games/{Games[0]}/mods/{ModId(Games[0], 7)}/files.json", $"/v1/games/{Games[0]}/mods/{ModId(Games[0], 21)}/files.json", $"/v1/games/{Games[3]}/mods/{ModId(Games[3], 40)}/files.json"],
            [.. _api.Requests.Where(path => path.EndsWith("/files.json", StringComparison.Ordinal))]);

        // Linking asked once, and nothing was refused.
        Assert.Equal(FakeNexusApi.HourlyLimit - 1 - 200 - 8, _api.HourlyRemaining);
        Assert.Equal(0, _api.Refused);
    }

    [Fact]
    public async Task WhenTheRateLimitRunsOutTheRestWaitForTheNextCheck()
    {
        await LinkAndFillTheLibraryAsync();
        _api.SetRemaining(hourly: 30, daily: 0);

        CliResult stopped = await RunAsync("library", "check");
        int askedWhileLimited = _api.Requests.Count;
        _api.ForgetRequests();
        _api.SetRemaining(FakeNexusApi.HourlyLimit, FakeNexusApi.DailyLimit);
        CliResult finished = await RunAsync("library", "check");

        Assert.Equal(Failed, stopped.ExitCode);
        Assert.Equal(["Checked 30 mods with 31 requests.", "No new updates."], Lines(stopped));
        Assert.Equal(30, askedWhileLimited);
        Assert.Equal(0, _api.Refused);
        string[] errors = ErrorLines(stopped);
        Assert.Equal(1 + Games.Length - 1, errors.Length);
        Assert.StartsWith(
            $"Couldn't check mod {ModId(Games[0], 31)} of {Games[0]} on nexus: The Nexus Mods API's rate limit for this account is used up until ",
            errors[0],
            StringComparison.Ordinal);
        Assert.All(
            errors[1..],
            error => Assert.Contains(" on nexus: Not checked: nexus refused an earlier question in this check. The Nexus Mods API's rate limit", error, StringComparison.Ordinal));

        // The 30 mods checked are asked about through the feed; the other 170 on their own.
        Assert.Equal(Done, finished.ExitCode);
        Assert.Equal("Checked 200 mods with 171 requests.", Lines(finished)[0]);
        Assert.Equal([$"/v1/games/{Games[0]}/mods/updated.json"], [.. _api.Requests.Where(path => path.EndsWith("/updated.json", StringComparison.Ordinal))]);
    }

    [Fact]
    public async Task MovingTheCheckedLibraryKeepsEveryRecordValid()
    {
        await LinkAndFillTheLibraryAsync();
        await RunAsync("library", "check");
        _api.PublishUpdate(Games[1], ModId(Games[1], 3), TimeProvider.System.GetUtcNow());
        await RunAsync("library", "check");
        string update = (FakeNexusApi.FirstFileId(ModId(Games[1], 3)) + 1).ToString(CultureInfo.InvariantCulture);
        string movedTo = Path.Combine(_cli.Folder, "Moved Library");

        CliResult moved = await RunAsync("library", "move", movedTo);
        CliResult updates = await RunAsync("library", "updates");
        (string root, IReadOnlyList<LibraryMod> library) = await _cli.InAppAsync(
            async services => (
                await services.GetRequiredService<ISettingsService>().GetLibraryRootAsync(Cancellation),
                await services.GetRequiredService<ILibraryRepository>().ListAsync(Cancellation)),
            Cancellation);

        Assert.Equal(Done, moved.ExitCode);
        Assert.Equal([$"Moved 200 files from {Root} to {movedTo}.", $"The library is in {movedTo} now."], Lines(moved));
        Assert.Equal(movedTo, root);
        Assert.Equal(200, library.Count);
        Assert.All(library, mod =>
        {
            LibraryFile file = Assert.Single(mod.Files);
            string path = LibraryPaths.Resolve(movedTo, file.RelativePath);
            Assert.True(File.Exists(path), $"{path} is missing.");
            Assert.Equal(file.SizeInBytes, new FileInfo(path).Length);
            Assert.False(File.Exists(LibraryPaths.Resolve(Root, file.RelativePath)));
            Assert.Equal(mod.Mod == Mod(Games[1], 3) ? [update] : [], mod.Updates.Select(found => found.File.Id));
        });
        Assert.Equal("1 update for 1 mod:", Lines(updates)[0]);
    }

    private static long ModId(string game, int number) => ((Array.IndexOf(Games, game) + 1) * 1000) + number;

    private static ModRef Mod(string game, int number)
        => new(new GameRef(new SourceId("nexus"), game), ModId(game, number).ToString(CultureInfo.InvariantCulture));

    private static string[] Lines(CliResult result) => result.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n');

    private static string[] ErrorLines(CliResult result) => result.Errors.TrimEnd().ReplaceLineEndings("\n").Split('\n');

    /// <summary>
    /// Links the Nexus Mods account with its API key, then puts each mod's main file into the library and on disk, as
    /// downloading it would.
    /// </summary>
    private async Task LinkAndFillTheLibraryAsync()
    {
        CliResult linked = await _cli.RunAsync(["account", "link", "nexus", "--with", "api-key"], Cancellation, FakeNexusApi.ApiKey + "\n");
        Assert.Equal(Done, linked.ExitCode);

        await _cli.InAppAsync(
            async services =>
            {
                ILibraryRepository library = services.GetRequiredService<ILibraryRepository>();
                DateTimeOffset now = TimeProvider.System.GetUtcNow();
                foreach (string game in Games)
                {
                    for (int number = 1; number <= ModsPerGame; number++)
                    {
                        ModRef mod = Mod(game, number);
                        long id = ModId(game, number);
                        string relativePath = $"nexus/{game}/Mod {id} ({id})/{FakeNexusApi.FileName(id, "1.0")}";
                        string path = LibraryPaths.Resolve(Root, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await File.WriteAllBytesAsync(path, Content, Cancellation);

                        LibraryFile file = new(
                            new ModFileRef(mod, FakeNexusApi.FirstFileId(id).ToString(CultureInfo.InvariantCulture)),
                            $"Mod {id} 1.0",
                            "1.0",
                            ModFileCategory.Main,
                            Uploaded,
                            relativePath,
                            Content.Length,
                            now);
                        await library.UpdateAsync(
                            mod,
                            _ => LibraryMod.Watch(mod, $"Mod {id}", "An Author", new Uri($"https://www.nexusmods.com/{game}/mods/{id}"), now).WithFile(file),
                            Cancellation);
                    }
                }

                return true;
            },
            Cancellation);
    }

    private Task<CliResult> RunAsync(params string[] arguments) => _cli.RunAsync(arguments, Cancellation);
}
