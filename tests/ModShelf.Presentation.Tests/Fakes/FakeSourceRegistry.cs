using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// The sources the test registers.
/// </summary>
internal sealed class FakeSourceRegistry(params IModSource[] sources) : ISourceRegistry
{
    public IEnumerable<T> All<T>()
        where T : IModSource
        => sources.OfType<T>();

    public T? Find<T>(SourceId id)
        where T : class, IModSource
        => sources.OfType<T>().FirstOrDefault(source => source.Id == id);
}
