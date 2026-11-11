using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Accounts;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Accounts;

/// <summary>
/// Settings › Accounts over fakes, with two sites that link accounts: Nexus Mods, which needs an account and links one
/// with an API key or in the browser, and mod.io, which works without one and links one with an emailed code; and two
/// that need none: DEG Mods, which searches, downloads and lists updates, and web links, which download.
/// </summary>
internal sealed class AccountsHarness
{
    public const string NexusName = "Nexus Mods";
    public const string ModIoName = "mod.io";

    public static readonly SourceId ModIo = new("mod-io");

    public static readonly SourceId DegMods = new("degmods");

    public static readonly SourceId WebLinks = new("direct");

    public static readonly Uri KeyPage = new("https://next.nexusmods.com/settings/api-keys");

    public static readonly Uri ApprovalPage = new("https://users.nexusmods.com/oauth/authorize?client_id=modshelf&state=abc");

    /// <param name="openSites">Whether DEG Mods and web links, which need no account, are sites too.</param>
    /// <param name="sites">The sites registered, in place of DEG Mods and web links.</param>
    public AccountsHarness(bool protectedByOs = true, bool openSites = true, IReadOnlyList<IModSource>? sites = null)
    {
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Screen = new AccountsViewModel(
            Accounts,
            new FixedSecretStore(protectedByOs),
            Browser,
            Dialogs,
            UiThread,
            Time,
            Pictures.Pictures,
            new FakeSourceRegistry([.. sites ?? (openSites ? [new OpenSite(DegMods, "DEG Mods"), new PageSite(WebLinks, "Web links")] : [])]),
            Wanting,
            Logger);
    }

    public ScriptedAccounts Accounts { get; } = new();

    public ScriptedBrowser Browser { get; } = new();

    public ScriptedDialogs Dialogs { get; } = new();

    public QueuedUiThread UiThread { get; } = new();

    public FakeTimeProvider Time { get; } = new(TestData.Now);

    public RecordingLogger<AccountsViewModel> Logger { get; } = new();

    public TestPictures Pictures { get; } = new();

    /// <summary>
    /// The sites whose downloads failed for want of an account, as the Downloads screen says.
    /// </summary>
    public SitesWantingAccounts Wanting { get; } = new();

    public AccountsViewModel Screen { get; }

    public static LinkedAccount Account(
        SourceId source, AuthKind kind = AuthKind.ApiKey, string name = "Jean", string? tier = "Premium", DateTimeOffset? linkedAt = null, Uri? picture = null)
        => new(source, kind, new AccountProfile(name, picture, tier), linkedAt ?? TestData.Now.AddDays(-7));

    /// <summary>
    /// Adds the two sites, with the accounts given, then lists them, as the main window starts the screen.
    /// </summary>
    public async Task StartAsync(LinkedAccount? nexus = null, LinkedAccount? modIo = null)
    {
        Accounts.AddSource(TestData.Nexus, NexusName, worksWithoutAccount: false, [AuthKind.ApiKey, AuthKind.OAuthPkce], nexus);
        Accounts.AddSource(ModIo, ModIoName, worksWithoutAccount: true, [AuthKind.EmailCode], modIo);
        await Screen.StartAsync(TestContext.Current.CancellationToken);
    }

    public AccountViewModel Card(SourceId source) => Assert.Single(Screen.Sources, card => card.Source == source);
}
