using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.Services;

/// <summary>
/// Whether two sites are in use: those the library has mods of, then those with an account linked.
/// </summary>
public sealed class SitesInUseTests
{
    private static readonly SourceId Example = ExampleSite.SourceId;

    private readonly InMemoryModLibrary _library = new();
    private readonly ScriptedAccounts _accounts = new();
    private readonly RecordingLogger<SitesInUse> _logger = new();
    private readonly SitesInUse _sites;

    public SitesInUseTests()
    {
        _sites = new SitesInUse(_library, _accounts, _logger);
    }

    [Fact]
    public async Task NoSiteIsNotSeveral()
    {
        Assert.False(await AreSeveralAsync());
    }

    [Fact]
    public async Task OneSitesModsAreNotSeveral()
    {
        _library.Add(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"), TestData.LibraryMod(TestData.Mod("2400", "stardewvalley"), "SMAPI"));

        Assert.False(await AreSeveralAsync());
    }

    [Fact]
    public async Task TwoSitesModsAreSeveralWithoutAskingTheAccounts()
    {
        _library.Add(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"), TestData.LibraryMod(ExampleSite.ModOf(ExampleSite.StardewValley, ExampleSite.BetterCrops), "Better Crops"));

        Assert.True(await AreSeveralAsync());
        Assert.Empty(_accounts.Calls);

        // From the library's games, never its every mod and file (ADR 0084).
        Assert.Equal(0, _library.ModsListed);
    }

    [Fact]
    public async Task AnAccountLinkedOnAnotherSiteMakesTwo()
    {
        _library.Add(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"));
        _accounts.AddSource(TestData.Nexus, "Nexus Mods", worksWithoutAccount: false, [AuthKind.ApiKey], Account(TestData.Nexus));
        _accounts.AddSource(Example, "Example Mods", worksWithoutAccount: true, [AuthKind.ApiKey], Account(Example));

        Assert.True(await AreSeveralAsync());
    }

    [Fact]
    public async Task TwoAccountsLinkedAreSeveralWithAnEmptyLibrary()
    {
        _accounts.AddSource(TestData.Nexus, "Nexus Mods", worksWithoutAccount: false, [AuthKind.ApiKey], Account(TestData.Nexus));
        _accounts.AddSource(Example, "Example Mods", worksWithoutAccount: true, [AuthKind.ApiKey], Account(Example));

        Assert.True(await AreSeveralAsync());
    }

    [Fact]
    public async Task ASiteWithoutAnAccountLinkedDoesNotCount()
    {
        _library.Add(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"));
        _accounts.AddSource(TestData.Nexus, "Nexus Mods", worksWithoutAccount: false, [AuthKind.ApiKey], Account(TestData.Nexus));
        _accounts.AddSource(Example, "Example Mods", worksWithoutAccount: true, [AuthKind.ApiKey]);

        Assert.False(await AreSeveralAsync());
    }

    [Fact]
    public async Task AnAccountOnTheModsOwnSiteIsStillOneSite()
    {
        _library.Add(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"));
        _accounts.AddSource(TestData.Nexus, "Nexus Mods", worksWithoutAccount: false, [AuthKind.ApiKey], Account(TestData.Nexus));

        Assert.False(await AreSeveralAsync());
        Assert.Equal(["list"], _accounts.Calls);
    }

    [Fact]
    public async Task ALibraryThatCantBeReadIsNotSeveralAndIsLogged()
    {
        _library.ListRefusal = new InvalidDataException("The library's database is damaged.");
        _accounts.AddSource(TestData.Nexus, "Nexus Mods", worksWithoutAccount: false, [AuthKind.ApiKey], Account(TestData.Nexus));
        _accounts.AddSource(Example, "Example Mods", worksWithoutAccount: true, [AuthKind.ApiKey], Account(Example));

        Assert.False(await AreSeveralAsync());
        Assert.StartsWith(
            "Warning: Couldn't tell which sites are in use, so the screens don't show sites System.IO.InvalidDataException: The library's database is damaged.",
            Assert.Single(_logger.Entries),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountsThatCantBeListedAreNotSeveralAndAreLogged()
    {
        _library.Add(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"));
        _accounts.ListRefusal = new IOException("The accounts file is locked.");

        Assert.False(await AreSeveralAsync());
        Assert.StartsWith("Warning: Couldn't tell which sites are in use", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoppingIsNotTakenForAProblem()
    {
        _library.ListRefusal = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(AreSeveralAsync);
        Assert.Empty(_logger.Entries);
    }

    private static LinkedAccount Account(SourceId source)
        => new(source, AuthKind.ApiKey, new AccountProfile("Jean", null, null), TestData.Now.AddDays(-7));

    private Task<bool> AreSeveralAsync() => _sites.AreSeveralAsync(TestContext.Current.CancellationToken);
}
