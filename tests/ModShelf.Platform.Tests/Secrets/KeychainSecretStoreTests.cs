using System.Globalization;
using System.Runtime.Versioning;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Platform.Secrets;

namespace ModShelf.Platform.Tests.Secrets;

/// <summary>
/// Runs against the real login Keychain, under a service name of its own that the tests clean up.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class KeychainSecretStoreTests : SecretStoreContractTests
{
    private readonly string _service = $"ModShelf.Tests.{Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)}";

    protected override string? SkipReason => OperatingSystem.IsMacOS() ? null : "The Keychain exists only on macOS.";

    [Fact]
    public void IsProtectedByTheOs()
    {
        Assert.True(Store.IsProtectedByOs);
    }

    [Fact]
    public async Task ServicesKeepTestRunsApart()
    {
        await WriteAsync("account/test", "secret");

        KeychainSecretStore otherService = new($"{_service}.other");

        Assert.Null(await otherService.ReadAsync("account/test", TestContext.Current.CancellationToken));
    }

    protected override ISecretStore CreateStore() => new KeychainSecretStore(_service);
}
