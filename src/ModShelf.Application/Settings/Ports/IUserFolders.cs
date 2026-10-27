namespace ModShelf.Application.Settings.Ports;

/// <summary>
/// The user's own folders, wherever their system keeps them. Implemented by Platform.
/// </summary>
public interface IUserFolders
{
    /// <summary>
    /// The absolute path of the user's Downloads folder, including when the user moved it.
    /// </summary>
    string Downloads { get; }
}
