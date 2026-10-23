using System.Runtime.Versioning;
using System.Text;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Platform.Secrets;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Secrets;

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStoreTests : SecretStoreContractTests
{
    private readonly TempFolder _folder = new();

    protected override string? SkipReason => OperatingSystem.IsWindows() ? null : "DPAPI exists only on Windows.";

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _folder.Dispose();
    }

    [Fact]
    public void IsProtectedByTheOs()
    {
        Assert.True(Store.IsProtectedByOs);
    }

    [Fact]
    public async Task TheFileDoesNotContainTheSecret()
    {
        await WriteAsync("account/test", "api-key-0f9e8d7c6b5a");

        byte[] file = await File.ReadAllBytesAsync(FileOf("account/test"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("api-key", Encoding.UTF8.GetString(file), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileCopiedOverAnotherSecretCannotBeRead()
    {
        await WriteAsync("account/nexus", "nexus-key");
        await WriteAsync("account/mod-io", "mod-io-key");

        File.Copy(FileOf("account/nexus"), FileOf("account/mod-io"), overwrite: true);

        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync("account/mod-io"));
    }

    protected override ISecretStore CreateStore() => new DpapiSecretStore(_folder.Path);

    private string FileOf(string name) => Path.Combine(_folder.Path, $"{name.Replace('/', '_')}.secret");
}
