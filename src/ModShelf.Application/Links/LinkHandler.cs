using ModShelf.Domain.Mods;

namespace ModShelf.Application.Links;

/// <summary>
/// A site that hands its downloads over with links of its own scheme, and which app opens them.
/// </summary>
/// <param name="Source">The site, such as <c>nexus</c>.</param>
/// <param name="SiteName">The site's name, such as "Nexus Mods".</param>
/// <param name="Scheme">Its links' scheme, such as <c>nxm</c>.</param>
/// <param name="State">Which app the system opens them with.</param>
public sealed record LinkHandler(SourceId Source, string SiteName, string Scheme, SchemeState State);
