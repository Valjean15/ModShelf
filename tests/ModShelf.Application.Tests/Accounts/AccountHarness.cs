using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Accounts;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Accounts;

/// <summary>
/// An <see cref="AccountService"/> with three sources: Nexus (OAuth or an API key), mod.io (an emailed code, and
/// usable without an account) and a direct-link source without accounts. <see cref="Restart"/> builds a new
/// service over the same stores, as a new run of the app would.
/// </summary>
internal sealed class AccountHarness
{
    public static readonly SourceId DirectId = new("direct");

    public AccountHarness()
    {
        NexusOAuth = FakeStrategy.OAuth(Time);
        NexusApiKey = FakeStrategy.ApiKey(Time);
        ModIoEmail = FakeStrategy.EmailCode(Time);
        Nexus = new FakeAccountSource(TestData.Nexus, NexusOAuth, NexusApiKey);
        ModIo = new FakeAccountSource(TestData.ModIo, ModIoEmail) { WorksWithoutAccount = true };
        Service = Build();
    }

    public FakeTimeProvider Time { get; } = new(TestData.Now);

    public FakeStrategy NexusOAuth { get; }

    public FakeStrategy NexusApiKey { get; }

    public FakeStrategy ModIoEmail { get; }

    public FakeAccountSource Nexus { get; }

    public FakeAccountSource ModIo { get; }

    public MemorySecretStore Secrets { get; } = new();

    public InMemoryLinkedAccountRepository Accounts { get; } = new();

    public FakeBrowser Browser { get; } = new();

    public ScriptedPrompt Prompt { get; } = new();

    public RecordingLogger<AccountService> Logger { get; } = new();

    public AccountService Service { get; private set; }

    public void Restart() => Service = Build();

    /// <summary>
    /// A service over the same stores, as another ModShelf process running at the same time has.
    /// </summary>
    public AccountService AnotherProcess() => Build();

    public Task<LinkedAccount> LinkAsync(SourceId source, AuthKind kind)
        => Service.LinkAsync(source, kind, Prompt, TestContext.Current.CancellationToken);

    private AccountService Build()
    {
        IModSource[] sources = [Nexus, ModIo, new ScriptedResolver(DirectId)];
        return new AccountService(new SourceRegistry(sources), Accounts, Secrets, Browser, Browser, Time, Logger);
    }
}
