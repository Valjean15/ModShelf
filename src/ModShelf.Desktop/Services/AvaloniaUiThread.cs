using Avalonia.Threading;

using ModShelf.Presentation.Services;

namespace ModShelf.Desktop.Services;

/// <summary>
/// Avalonia's UI thread: what's posted from it runs at once, and what's posted from elsewhere is queued to it.
/// </summary>
internal sealed class AvaloniaUiThread : IUiThread
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
