using System.Globalization;
using System.Runtime.Versioning;

using ModShelf.Application.Links;
using ModShelf.Platform.Links;

namespace ModShelf.Platform.Tests.Links;

/// <summary>
/// Launch Services, from a test run, which isn't an app bundle: it names the apps, but can't register.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class LaunchServicesSchemeRegistrationTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void NamesTheAppThatOpensWebLinks()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Launch Services exists only on macOS.");

        Assert.False(string.IsNullOrWhiteSpace(LaunchServicesInterop.DefaultHandler("https")));
        Assert.Null(LaunchServicesInterop.DefaultHandler(UnknownScheme()));
    }

    [Fact]
    public async Task OutsideAnAppBundleItCantRegister()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Launch Services exists only on macOS.");
        Assert.SkipWhen(LaunchServicesInterop.MainBundleId() is not null, "This test run is an app bundle.");
        LaunchServicesSchemeRegistration registration = new();
        string scheme = UnknownScheme();

        Assert.Equal(SchemeState.Unavailable, await registration.GetAsync(scheme, Cancellation));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registration.RegisterAsync(scheme, Cancellation));
        await registration.UnregisterAsync(scheme, Cancellation);
        Assert.Null(LaunchServicesInterop.DefaultHandler(scheme));
    }

    private static string UnknownScheme() => "modshelf-test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
}
