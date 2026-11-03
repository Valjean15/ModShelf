namespace ModShelf.Cli.Mcp;

/// <summary>
/// Whether the update was skipped or offered again, in a sentence too, the update as it is now, and its mod as the
/// library has it.
/// </summary>
/// <param name="Update">The update skipped or offered again; null when that changed nothing.</param>
internal sealed record SkipResult(bool Skip, string Message, LibraryUpdateEntry? Update, LibraryModEntry Mod);
