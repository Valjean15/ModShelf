using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// A mod whose files on disk couldn't be matched, and why, such as its site not answering.
/// </summary>
public sealed record RescanFailure(ModRef Mod, string Reason);
