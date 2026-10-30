namespace ModShelf.Platform.Links;

/// <summary>
/// Where the desktop app is, for the system to start it with a link: beside this app, or on Linux the AppImage it runs
/// from. An AppImage runs from a folder mounted afresh at each start, so only the AppImage itself stays where it is.
/// </summary>
internal static class DesktopAppPath
{
    /// <param name="folder">This app's folder.</param>
    /// <param name="appImage">The <c>APPIMAGE</c> variable, the AppImage's path, which its runtime sets.</param>
    /// <param name="appDir">
    /// The <c>APPDIR</c> variable, the folder the AppImage is mounted at. Another app's AppImage, such as a terminal's,
    /// sets both for what it starts, so this app runs from its own AppImage only when its folder is in that one.
    /// </param>
    public static string Of(string folder, string? appImage, string? appDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        bool inAppImage = OperatingSystem.IsLinux()
            && !string.IsNullOrEmpty(appImage)
            && !string.IsNullOrEmpty(appDir)
            && Path.GetFullPath(folder).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir)) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && File.Exists(appImage);
        return inAppImage ? appImage! : Path.Combine(folder, OperatingSystem.IsWindows() ? "ModShelf.Desktop.exe" : "ModShelf.Desktop");
    }
}
