using System.Runtime.Versioning;

using Microsoft.Extensions.Logging.Abstractions;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Platform.Secrets;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Secrets;

public sealed class SecretStoresTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void PicksTheStoreForThisSystem()
    {
        ISecretStore store = SecretStores.ForThisSystem(_folder.Path, SecretStoreOptions.DefaultScope, NullLogger.Instance);

        Type expected = OperatingSystem.IsWindows() ? typeof(DpapiSecretStore)
            : OperatingSystem.IsMacOS() ? typeof(KeychainSecretStore)
            : OperatingSystem.IsLinux() && LibSecretSecretStore.Probe() is not { NoKeyring: true } ? typeof(LibSecretSecretStore)
            : typeof(EncryptedFileSecretStore);
        Assert.IsType(expected, store);
        Assert.Equal(expected != typeof(EncryptedFileSecretStore), store.IsProtectedByOs);
    }

    [Fact]
    public async Task ScopesKeepSecretsApartInTheSystemKeyring()
    {
        string scope = $"ModShelf.Tests.{Guid.NewGuid():N}";
        ISecretStore store = SecretStores.ForThisSystem(_folder.Path, scope, NullLogger.Instance);
        Assert.SkipUnless(
            store.IsProtectedByOs && !OperatingSystem.IsWindows(),
            "Scopes apply to the Keychain and the Secret Service; files are kept apart by their folder.");
        ISecretStore otherScope = SecretStores.ForThisSystem(_folder.Path, scope + ".other", NullLogger.Instance);
        CancellationToken cancellation = TestContext.Current.CancellationToken;

        await store.WriteAsync("account/test", "secret", cancellation);
        try
        {
            Assert.Null(await otherScope.ReadAsync("account/test", cancellation));
            Assert.Equal("secret", await store.ReadAsync("account/test", cancellation));
        }
        finally
        {
            await store.DeleteAsync("account/test", cancellation);
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void OnLinuxTheSecretServiceIsUsedWhenItAnswers()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Secret Service is Linux's.");
        RecordingLogger<SecretStoresTests> logger = new();

        ISecretStore store = SecretStores.ForLinux(_folder.Path, SecretStoreOptions.DefaultScope, null, logger);

        Assert.IsType<LibSecretSecretStore>(store);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void OnLinuxWithoutAKeyringSecretsGoToEncryptedFiles()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Secret Service is Linux's.");
        RecordingLogger<SecretStoresTests> logger = new();

        ISecretStore store = SecretStores.ForLinux(
            _folder.Path,
            SecretStoreOptions.DefaultScope,
            (true, "The name org.freedesktop.secrets was not provided by any .service files"),
            logger);

        Assert.IsType<EncryptedFileSecretStore>(store);
        Assert.StartsWith("Warning: No keyring (Secret Service) is running", Assert.Single(logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void OnLinuxAKeyringThatFailsIsStillUsedAndThatsLogged()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Secret Service is Linux's.");
        RecordingLogger<SecretStoresTests> logger = new();

        ISecretStore store = SecretStores.ForLinux(_folder.Path, SecretStoreOptions.DefaultScope, (false, "Timeout was reached"), logger);

        Assert.IsType<LibSecretSecretStore>(store);
        Assert.StartsWith(
            "Error: The keyring (Secret Service) didn't answer as it should, so ModShelf can't read or save credentials until it does: Timeout was reached.",
            Assert.Single(logger.Entries),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("g-dbus-error-quark", 2, true)]
    [InlineData("g-dbus-error-quark", 3, true)]
    [InlineData("g-dbus-error-quark", 11, true)]
    [InlineData("g-dbus-error-quark", 9, false)]
    [InlineData("g-dbus-error-quark", 25, false)]
    [InlineData("g-io-error-quark", 0, true)]
    [InlineData("g-io-error-quark", 1, true)]
    [InlineData("g-io-error-quark", 19, false)]
    [InlineData("g-io-error-quark", 24, false)]
    [InlineData("secret-error", 2, false)]
    [InlineData(null, 0, false)]
    [SupportedOSPlatform("linux")]
    public void OnlyAnErrorFindingNoSecretServiceMeansThereIsNone(string? domain, int code, bool none)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Secret Service is Linux's.");

        Assert.Equal(none, LibSecretSecretStore.MeansNoKeyring(domain, code));
    }

    [Fact]
    public void TheDefaultFolderIsUnderTheUsersLocalAppData()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModShelf", "Secrets"),
            SecretStores.GetDefaultFolder());
    }
}
