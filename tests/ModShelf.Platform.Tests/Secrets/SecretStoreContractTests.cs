using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Platform.Tests.Secrets;

/// <summary>
/// What every secret store must do. Each store's tests derive from this; a store that can't run on this machine,
/// such as the Keychain off macOS, reports its tests as skipped.
/// </summary>
public abstract class SecretStoreContractTests : IAsyncLifetime
{
    private readonly List<string> _written = [];
    private ISecretStore? _store;

    public static TheoryData<string> InvalidNames =>
    [
        "",
        "Account/Nexus",
        "account//nexus",
        "/account",
        "account/",
        "account/..",
        "../account",
        "account nexus",
        "account_nexus",
        new string('a', 129),
    ];

    public static TheoryData<string> Secrets =>
    [
        "api-key-0f9e8d7c6b5a",
        "Ünïcödé ✓ 秘密",
        """{"accessToken":"a.b.c","refreshToken":"r","expiresAt":"2026-10-23T18:00:00+00:00"}""",
        "line one\nline two\r\n",
        new string('x', 8000),
    ];

    /// <summary>
    /// Why the store can't run on this machine, or null when it can.
    /// </summary>
    protected abstract string? SkipReason { get; }

    /// <summary>
    /// The store under test. Skips the test when the store can't run here.
    /// </summary>
    protected ISecretStore Store
    {
        get
        {
            Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);
            return _store ??= CreateStore();
        }
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public virtual async ValueTask DisposeAsync()
    {
        // Leave nothing behind in the system's real keyring.
        if (_store is not null)
        {
            foreach (string name in _written)
            {
                await _store.DeleteAsync(name, CancellationToken.None);
            }
        }

        GC.SuppressFinalize(this);
    }

    [Theory]
    [MemberData(nameof(Secrets))]
    public async Task ReadsBackWhatItWrote(string secret)
    {
        await WriteAsync("account/test", secret);

        Assert.Equal(secret, await ReadAsync("account/test"));
    }

    [Fact]
    public async Task HasNothingUnderANameNeverWritten()
    {
        Assert.Null(await ReadAsync("account/never-written"));
    }

    [Fact]
    public async Task WritingAgainReplacesTheSecret()
    {
        await WriteAsync("account/test", "first");
        await WriteAsync("account/test", "second");

        Assert.Equal("second", await ReadAsync("account/test"));
    }

    [Fact]
    public async Task DeleteRemovesTheSecret()
    {
        await WriteAsync("account/test", "secret");

        await Store.DeleteAsync("account/test", TestContext.Current.CancellationToken);

        Assert.Null(await ReadAsync("account/test"));
    }

    [Fact]
    public async Task DeletingASecretThatDoesNotExistDoesNothing()
    {
        await Store.DeleteAsync("account/never-written", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task KeepsSecretsWithDifferentNamesApart()
    {
        await WriteAsync("account/nexus", "nexus-key");
        await WriteAsync("account/mod-io", "mod-io-key");
        await Store.DeleteAsync("account/nexus", TestContext.Current.CancellationToken);

        Assert.Null(await ReadAsync("account/nexus"));
        Assert.Equal("mod-io-key", await ReadAsync("account/mod-io"));
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task RejectsAnInvalidName(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Store.WriteAsync(name, "secret", cancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Store.ReadAsync(name, cancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Store.DeleteAsync(name, cancellationToken));
    }

    protected abstract ISecretStore CreateStore();

    protected async Task WriteAsync(string name, string secret)
    {
        _written.Add(name);
        await Store.WriteAsync(name, secret, TestContext.Current.CancellationToken);
    }

    protected Task<string?> ReadAsync(string name) => Store.ReadAsync(name, TestContext.Current.CancellationToken);
}
