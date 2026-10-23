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
            : OperatingSystem.IsLinux() && LibSecretSecretStore.IsAvailable() ? typeof(LibSecretSecretStore)
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
    public void TheDefaultFolderIsUnderTheUsersLocalAppData()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModShelf", "Secrets"),
            SecretStores.GetDefaultFolder());
    }
}
