namespace ModShelf.Infrastructure.Logging;

/// <summary>
/// The folder the log files go to, to tell the user where to find them.
/// </summary>
/// <param name="Path">The folder's absolute path.</param>
public sealed record LogFolder(string Path);
