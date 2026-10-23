using Microsoft.Extensions.Logging.Abstractions;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Platform.Secrets;
using ModShelf.Platform.Tests.Fakes;

namespace ModShelf.Platform.Tests.Secrets;

public sealed class SecretStoresTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void PicksTheStoreForThisSystem()
    {
        ISecretStore store = SecretStores.ForThisSystem(_folder.Path, NullLogger.Instance);

        Type expected = OperatingSystem.IsWindows() ? typeof(DpapiSecretStore)
            : OperatingSystem.IsMacOS() ? typeof(KeychainSecretStore)
            : OperatingSystem.IsLinux() && LibSecretSecretStore.IsAvailable() ? typeof(LibSecretSecretStore)
            : typeof(EncryptedFileSecretStore);
        Assert.IsType(expected, store);
        Assert.Equal(expected != typeof(EncryptedFileSecretStore), store.IsProtectedByOs);
    }

    [Fact]
    public void TheDefaultFolderIsUnderTheUsersLocalAppData()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModShelf", "Secrets"),
            SecretStores.GetDefaultFolder());
    }
}
