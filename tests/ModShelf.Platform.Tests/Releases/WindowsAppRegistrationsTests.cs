using ModShelf.Platform.Notifications;
using ModShelf.Platform.Releases;
using ModShelf.Platform.Tests.Links;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Releases;

/// <summary>
/// What the uninstaller removes on Windows of what ModShelf registered for itself, over an in-memory registry.
/// </summary>
public sealed class WindowsAppRegistrationsTests
{
    private readonly MemoryRegistryTree _user = new();
    private readonly MemorySchemeRegistration _links = new();
    private readonly RecordingLogger<WindowsAppRegistrationsTests> _logger = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RemovesTheNoticesAppIdAndTheirLinks()
    {
        _user.SetString(ToastAppId.KeyOf(ToastAppId.Default), "DisplayName", "ModShelf");
        _user.SetString(ToastAppId.KeyOf("Another.App"), "DisplayName", "Another app");
        NoticeClicks clicks = new(_links, _logger);
        await clicks.LinkAsync(Cancellation);

        await new WindowsAppRegistrations(_user, clicks).RemoveAsync(Cancellation);

        Assert.False(_user.Exists(@"Software\Classes\AppUserModelId\ModShelf.App"));
        Assert.True(_user.Exists(ToastAppId.KeyOf("Another.App")));
        Assert.Equal(["Register modshelf", "Unregister modshelf"], _links.Calls);
    }

    [Fact]
    public async Task NothingRegisteredIsNothingToRemove()
    {
        await new WindowsAppRegistrations(_user, new NoticeClicks(_links, _logger)).RemoveAsync(Cancellation);

        Assert.False(_user.Exists(ToastAppId.KeyOf(ToastAppId.Default)));
        Assert.Equal(["Unregister modshelf"], _links.Calls);
    }

    [Fact]
    public async Task OtherSystemsKeepNothingForModShelf()
    {
        Assert.IsType(
            OperatingSystem.IsWindows() ? typeof(WindowsAppRegistrations) : typeof(NoAppRegistrations),
            AppRegistrations.ForThisSystem(_links, _logger));

        await new NoAppRegistrations().RemoveAsync(Cancellation);
        Assert.Empty(_links.Calls);
    }
}
