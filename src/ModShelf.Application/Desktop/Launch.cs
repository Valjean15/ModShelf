namespace ModShelf.Application.Desktop;

/// <summary>
/// A launch of the desktop app, handed over to the one running.
/// </summary>
/// <param name="Arguments">Its command-line arguments, such as a link it was started with.</param>
public sealed record Launch(IReadOnlyList<string> Arguments);
