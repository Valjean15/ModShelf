using System.Runtime.Versioning;

using ModShelf.Application.Settings.Ports;

namespace ModShelf.Platform.Folders;

/// <summary>
/// The user's folders on Windows, through the Known Folders API: a Downloads folder the user moved to another drive is
/// found there.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class KnownFolderUserFolders : IUserFolders
{
    public string Downloads
        => KnownFolderInterop.PathOf(KnownFolderInterop.Downloads)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}
