using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions;

/// <summary>
/// A mod site that ModShelf can work with. A source implements only the capability interfaces its
/// site supports, such as <see cref="Downloads.IDownloadResolver"/>.
/// </summary>
public interface IModSource
{
    /// <summary>
    /// The source's stable key, such as <c>nexus</c>.
    /// </summary>
    SourceId Id { get; }

    /// <summary>
    /// The site's name as shown to the user, such as "Nexus Mods".
    /// </summary>
    string DisplayName { get; }
}
