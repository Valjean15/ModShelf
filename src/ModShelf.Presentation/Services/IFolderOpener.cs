namespace ModShelf.Presentation.Services;

/// <summary>
/// Opens folders in the system's file manager. Desktop opens them through Avalonia; ViewModel tests record them.
/// </summary>
public interface IFolderOpener
{
    /// <param name="folder">The folder's full path.</param>
    /// <returns>False when the system couldn't open it, such as a folder that isn't there.</returns>
    Task<bool> TryOpenAsync(string folder, CancellationToken cancellationToken);
}
