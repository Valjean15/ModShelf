using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using ModShelf.Presentation.Services;

namespace ModShelf.Desktop.Services;

/// <summary>
/// Opens folders in the system's file manager through Avalonia's launcher, which belongs to a window.
/// </summary>
internal sealed class AvaloniaFolderOpener : IFolderOpener
{
    public Task<bool> TryOpenAsync(string folder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(folder))
        {
            return Task.FromResult(false);
        }

        return Dispatcher.UIThread.InvokeAsync(async () =>
            AppWindows.Main is TopLevel window && await window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder)));
    }
}
