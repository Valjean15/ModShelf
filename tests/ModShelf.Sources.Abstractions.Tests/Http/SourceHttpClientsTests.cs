using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Http;

namespace ModShelf.Sources.Abstractions.Tests.Http;

public sealed class SourceHttpClientsTests
{
    [Theory]
    [InlineData("nexus")]
    [InlineData("mod-io")]
    [InlineData("curseforge2")]
    public void TheNameLeadsBackToItsSource(string id)
    {
        SourceId source = new(id);

        Assert.True(SourceHttpClients.TryGetSource(SourceHttpClients.NameFor(source), out SourceId? found));
        Assert.Equal(source, found);
    }

    [Fact]
    public void EachSourceGetsItsOwnClient()
    {
        Assert.NotEqual(SourceHttpClients.NameFor(new SourceId("nexus")), SourceHttpClients.NameFor(new SourceId("mod-io")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nexus")]
    [InlineData("ModShelf.FileTransfer")]
    [InlineData("ModShelf.Source.")]
    [InlineData("ModShelf.Source.Nexus")]
    [InlineData("modshelf.source.nexus")]
    [InlineData("ModShelf.Source.nexus.extra")]
    public void OtherClientsBelongToNoSource(string? clientName)
    {
        Assert.False(SourceHttpClients.TryGetSource(clientName, out SourceId? source));
        Assert.Null(source);
    }
}
