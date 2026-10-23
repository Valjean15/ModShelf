using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Nexus.Resources;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// Nexus Mods (<c>nexusmods.com</c>), through its public API. Every request goes through the client the app sets
/// up for the source, which adds the linked account.
/// </summary>
public sealed class NexusSource : IModSource
{
    /// <summary>
    /// The source's ID. It's stored with every job and linked account, so it never changes.
    /// </summary>
    internal static readonly SourceId NexusId = new("nexus");

    public SourceId Id => NexusId;

    public string DisplayName => Strings.DisplayName;
}
