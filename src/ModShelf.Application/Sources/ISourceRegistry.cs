using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;

namespace ModShelf.Application.Sources;

/// <summary>
/// Finds the registered sources by capability.
/// </summary>
public interface ISourceRegistry
{
    /// <summary>
    /// Every registered source that implements <typeparamref name="T"/>.
    /// </summary>
    IEnumerable<T> All<T>()
        where T : IModSource;

    /// <summary>
    /// The source with the given ID if it implements <typeparamref name="T"/>; otherwise null.
    /// </summary>
    T? Find<T>(SourceId id)
        where T : class, IModSource;
}
