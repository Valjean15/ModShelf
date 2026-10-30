namespace ModShelf.Application.Diagnostics.Ports;

/// <summary>
/// Where ModShelf keeps its own files.
/// </summary>
public interface IAppFolders
{
    /// <summary>
    /// The folder of the library database and the settings, an absolute path.
    /// </summary>
    string DataFolder { get; }

    /// <summary>
    /// The folder of the log files, an absolute path.
    /// </summary>
    string LogFolder { get; }
}
