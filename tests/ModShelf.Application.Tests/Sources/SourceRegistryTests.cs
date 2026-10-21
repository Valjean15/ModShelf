using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Application.Tests.Sources;

public sealed class SourceRegistryTests
{
    private static readonly ScriptedResolver Nexus = new(TestData.Nexus);
    private static readonly CatalogOnlySource ModIo = new(TestData.ModIo);

    [Fact]
    public void AllReturnsTheSourcesWithTheCapability()
    {
        SourceRegistry registry = new([Nexus, ModIo]);

        Assert.Same(Nexus, Assert.Single(registry.All<IDownloadResolver>()));
        Assert.Equal(2, registry.All<IModSource>().Count());
    }

    [Fact]
    public void FindReturnsTheSourceOnlyWhenItHasTheCapability()
    {
        SourceRegistry registry = new([Nexus, ModIo]);

        Assert.Same(Nexus, registry.Find<IDownloadResolver>(TestData.Nexus));
        Assert.Null(registry.Find<IDownloadResolver>(TestData.ModIo));
        Assert.Null(registry.Find<IDownloadResolver>(new SourceId("modrinth")));
    }

    [Fact]
    public void RejectsTwoSourcesWithTheSameId()
    {
        Assert.Throws<ArgumentException>(() => new SourceRegistry([Nexus, new ScriptedResolver(TestData.Nexus)]));
    }

    /// <summary>
    /// A source that can't resolve downloads.
    /// </summary>
    private sealed class CatalogOnlySource(SourceId id) : IModSource
    {
        public SourceId Id { get; } = id;

        public string DisplayName => Id.Value;
    }
}
