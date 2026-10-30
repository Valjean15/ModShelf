using ModShelf.Application.Links.Ports;

namespace ModShelf.Platform.Links;

/// <summary>
/// Picks the register of link schemes for this system.
/// </summary>
internal static class SchemeRegistrations
{
    public static ISchemeRegistration ForThisSystem(SchemeHandlerOptions options)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSchemeRegistration(
                new WindowsRegistryTree(Microsoft.Win32.Registry.CurrentUser),
                new WindowsRegistryTree(Microsoft.Win32.Registry.LocalMachine),
                options);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new LaunchServicesSchemeRegistration();
        }

        return OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD()
            ? new XdgSchemeRegistration(XdgDirectories.ForThisUser(), options)
            : new UnavailableSchemeRegistration();
    }
}
