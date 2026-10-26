using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A site with a catalog but no search, for telling the two capabilities apart. It's never asked anything.
/// </summary>
internal sealed class CatalogOnlySource(SourceId id) : IModCatalog
{
    public SourceId Id { get; } = id;

    public string DisplayName => Id.Value;

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();
}
