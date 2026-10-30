using System.Text;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Platform.Secrets;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Secrets;

public sealed class EncryptedFileSecretStoreTests : SecretStoreContractTests
{
    private readonly TempFolder _folder = new();

    protected override string? SkipReason => null;

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public void IsNotProtectedByTheOs()
    {
        Assert.False(Store.IsProtectedByOs);
    }

    [Fact]
    public async Task TheFileDoesNotContainTheSecret()
    {
        await WriteAsync("account/test", "api-key-0f9e8d7c6b5a");

        byte[] file = await File.ReadAllBytesAsync(FileOf("account/test"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("api-key", Encoding.UTF8.GetString(file), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnotherComputerCannotReadTheFile()
    {
        EncryptedFileSecretStore here = new(_folder.Path, machineId: "machine-a");
        EncryptedFileSecretStore elsewhere = new(_folder.Path, machineId: "machine-b");
        await here.WriteAsync("account/test", "secret", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => elsewhere.ReadAsync("account/test", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileCopiedOverAnotherSecretCannotBeRead()
    {
        await WriteAsync("account/nexus", "nexus-key");
        await WriteAsync("account/mod-io", "mod-io-key");

        File.Copy(FileOf("account/nexus"), FileOf("account/mod-io"), overwrite: true);

        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync("account/mod-io"));
    }

    [Fact]
    public async Task ADamagedFileIsReported()
    {
        await WriteAsync("account/test", "secret");
        byte[] file = await File.ReadAllBytesAsync(FileOf("account/test"), TestContext.Current.CancellationToken);
        file[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(FileOf("account/test"), file, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync("account/test"));
    }

    [Fact]
    public async Task AMachineIdThatCantBeReadIsReadAgainNextTime()
    {
        int reads = 0;
        EncryptedFileSecretStore store = new(_folder.Path, readMachineId: () => ++reads == 1 ? throw new IOException("Busy.") : "machine-a");
        EncryptedFileSecretStore same = new(_folder.Path, machineId: "machine-a");
        CancellationToken cancellation = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync("account/test", "secret", cancellation));
        await store.WriteAsync("account/test", "secret", cancellation);
        await store.ReadAsync("account/test", cancellation);

        Assert.Equal("secret", await same.ReadAsync("account/test", cancellation));
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task WithoutASystemMachineIdARandomOneIsKeptBesideTheSecrets()
    {
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        await WithoutSystemId().WriteAsync("account/test", "secret", cancellation);

        Assert.Equal("secret", await WithoutSystemId().ReadAsync("account/test", cancellation));
        Assert.Matches("^[0-9a-f]{32}$", (await File.ReadAllTextAsync(Path.Combine(_folder.Path, ".machine-id"), cancellation)).Trim());
    }

    [Fact]
    public async Task AMachineIdAnotherProcessKeptFirstIsTheOneUsed()
    {
        // As when two processes make one at once: the one finished first stays.
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(_folder.Path, ".machine-id"), "machine-a\n", cancellation);
        await new EncryptedFileSecretStore(_folder.Path, machineId: "machine-a").WriteAsync("account/test", "secret", cancellation);

        Assert.Equal("secret", await WithoutSystemId().ReadAsync("account/test", cancellation));
        Assert.Equal("machine-a\n", await File.ReadAllTextAsync(Path.Combine(_folder.Path, ".machine-id"), cancellation));
        Assert.Empty(Directory.EnumerateFiles(_folder.Path, "*.tmp"));
    }

    [Fact]
    public async Task OnlyTheUserCanReadTheFiles()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file permissions don't apply on Windows.");

        await WriteAsync("account/test", "secret");

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FileOf("account/test")));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(Path.GetDirectoryName(FileOf("account/test"))!));
        }
    }

    protected override ISecretStore CreateStore() => new EncryptedFileSecretStore(Path.Combine(_folder.Path, "Secrets"));

    private string FileOf(string name) => Path.Combine(_folder.Path, "Secrets", $"{name.Replace('/', '_')}.secret");

    /// <summary>
    /// A store on a system without a machine ID of its own, as in some containers.
    /// </summary>
    private EncryptedFileSecretStore WithoutSystemId()
        => new(_folder.Path, readMachineId: null, systemIdFiles: [Path.Combine(_folder.Path, "no-machine-id")]);
}
