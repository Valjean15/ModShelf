using ModShelf.Application.Releases.Ports;
using ModShelf.Platform.Links;
using ModShelf.Platform.Notifications;

namespace ModShelf.Platform.Releases;

/// <summary>
/// What ModShelf registers for itself on Windows, for the current user: the app ID its toasts go by, and the
/// <c>modshelf://</c> links a click on one opens.
/// </summary>
internal sealed class WindowsAppRegistrations(IRegistryTree user, NoticeClicks clicks) : IAppRegistrations
{
    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        await clicks.RemoveAsync(cancellationToken).ConfigureAwait(false);
        user.DeleteTree(ToastAppId.KeyOf(ToastAppId.Default));
    }
}
