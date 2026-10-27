using ModShelf.Application.Settings.Ports;

namespace ModShelf.Platform.Folders;

/// <summary>
/// Chooses how to find the user's folders on this system.
/// </summary>
internal static class UserFolders
{
    public static IUserFolders ForThisSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return new KnownFolderUserFolders();
        }

        if (OperatingSystem.IsMacOS())
        {
            // The Finder shows Downloads in the user's language, but the folder's name is always the English one.
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new XdgUserFolders(home, Path.Combine(home, ".config"));
        }

        return XdgUserFolders.ForThisUser();
    }
}
