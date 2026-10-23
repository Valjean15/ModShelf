using System.Globalization;
using System.Runtime.Versioning;

using ModShelf.Application.Accounts;
using ModShelf.Platform.Secrets;

namespace ModShelf.Platform.Tests.Secrets;

/// <summary>
/// Runs against the real Secret Service. CI's Linux job starts GNOME Keyring for these; elsewhere, without a
/// running keyring, they're skipped.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LibSecretSecretStoreTests : SecretStoreContractTests
{
    private static readonly Lazy<bool> Available = new(() => OperatingSystem.IsLinux() && LibSecretSecretStore.IsAvailable());

    private readonly string _scope = $"ModShelf.Tests.{Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)}";

    protected override string? SkipReason
        => Available.Value ? null : "No Secret Service is running (it needs Linux with a keyring such as GNOME Keyring).";

    [Fact]
    public void IsProtectedByTheOs()
    {
        Assert.True(Store.IsProtectedByOs);
    }

    [Fact]
    public async Task ScopesKeepTestRunsApart()
    {
        await WriteAsync("account/test", "secret");

        LibSecretSecretStore otherScope = new($"{_scope}.other");

        Assert.Null(await otherScope.ReadAsync("account/test", TestContext.Current.CancellationToken));
    }

    protected override ISecretStore CreateStore() => new LibSecretSecretStore(_scope);
}
