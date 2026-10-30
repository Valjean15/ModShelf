using ModShelf.Application.Diagnostics.Ports;

namespace ModShelf.Infrastructure;

/// <summary>
/// Where ModShelf's own files are: the folder of the library database and the settings, and the log folder in it.
/// </summary>
internal sealed record AppFolders(string DataFolder, string LogFolder) : IAppFolders;
