using Microsoft.Extensions.Logging;

using ModShelf.Application.Links.Ports;
using ModShelf.Application.Releases.Ports;
using ModShelf.Platform.Links;
using ModShelf.Platform.Notifications;

namespace ModShelf.Platform.Releases;

/// <summary>
/// Picks what ModShelf registers for itself on this system.
/// </summary>
internal static class AppRegistrations
{
    /// <param name="links">The system's register of link schemes, where ModShelf registers its own.</param>
    public static IAppRegistrations ForThisSystem(ISchemeRegistration links, ILogger logger)
        => OperatingSystem.IsWindows()
            ? new WindowsAppRegistrations(new WindowsRegistryTree(Microsoft.Win32.Registry.CurrentUser), new NoticeClicks(links, logger))
            : new NoAppRegistrations();
}
