using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Common;

/// <summary>
/// A site whose games couldn't be read, and why.
/// </summary>
internal sealed record UnreadSite(SourceId Site, string Name, Exception Problem);
