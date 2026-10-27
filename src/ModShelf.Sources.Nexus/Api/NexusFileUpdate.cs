namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// One link of a mod's update chains, from the list of a mod's files: the author uploaded <paramref name="NewFileId"/>
/// as the update of <paramref name="OldFileId"/>.
/// </summary>
internal sealed record NexusFileUpdate(long OldFileId, long NewFileId);
