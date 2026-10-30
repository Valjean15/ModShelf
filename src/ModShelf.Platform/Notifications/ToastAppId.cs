namespace ModShelf.Platform.Notifications;

/// <summary>
/// The app ID ModShelf's Windows toasts go by, and the key that registers one for the current user. The installer's
/// Start-menu shortcut and the running app carry the same ID.
/// </summary>
internal static class ToastAppId
{
    /// <summary>
    /// ModShelf's app ID. Settings > System > Notifications lists ModShelf under it once a toast has shown.
    /// </summary>
    public const string Default = "ModShelf.App";

    private const string AppIdsKey = @"Software\Classes\AppUserModelId";

    /// <summary>
    /// The registry key, under HKEY_CURRENT_USER, that registers an app ID with the name its toasts show.
    /// </summary>
    public static string KeyOf(string applicationId) => $@"{AppIdsKey}\{applicationId}";
}
