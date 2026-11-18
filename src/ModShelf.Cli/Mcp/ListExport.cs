namespace ModShelf.Cli.Mcp;

/// <summary>
/// The library as a list, in ModShelf's list format, and what it holds.
/// </summary>
/// <param name="List">The list as JSON text, in the <c>modshelf-list</c> format: to save as a file, or give library_import.</param>
/// <param name="Mods">How many mods it lists.</param>
/// <param name="Files">How many files it lists.</param>
internal sealed record ListExport(string List, int Mods, int Files, string Message);
