using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;

namespace ModShelf.Application.Sources;

/// <summary>
/// The <see cref="ISourceRegistry"/> over the sources registered in the DI container.
/// </summary>
public sealed class SourceRegistry : ISourceRegistry
{
    private readonly IModSource[] _sources;

    public SourceRegistry(IEnumerable<IModSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = [.. sources];

        IGrouping<SourceId, IModSource>? duplicate = _sources.GroupBy(source => source.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"More than one source has the ID '{duplicate.Key}'.", nameof(sources));
        }
    }

    public IEnumerable<T> All<T>()
        where T : IModSource
        => _sources.OfType<T>();

    public T? Find<T>(SourceId id)
        where T : class, IModSource
    {
        ArgumentNullException.ThrowIfNull(id);

        return Array.Find(_sources, source => source.Id == id) as T;
    }
}
