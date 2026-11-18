using ModShelf.Application.Downloads;
using ModShelf.Application.Endorsements;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Endorsements;

namespace ModShelf.Application.Tests.Endorsements;

/// <summary>
/// Endorsing through the site a mod or a link belongs to, and reading what the account said there.
/// </summary>
public sealed class SiteEndorsementsTests
{
    private static readonly SourceId Endorsing = new("endorsing");
    private static readonly SourceId Following = new("following");

    private readonly FakeEndorsingSite _site = new(Endorsing);
    private readonly SiteEndorsements _endorsements;

    public SiteEndorsementsTests()
    {
        _endorsements = new SiteEndorsements(new SourceRegistry([_site, new FakeWatchlistSource(Following, recognizesLinks: false)]));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void SaysWhichSitesCanEndorse()
    {
        Assert.Equal((true, false, false), (_endorsements.CanEndorse(Endorsing), _endorsements.CanEndorse(Following), _endorsements.CanEndorse(new SourceId("none"))));
    }

    [Fact]
    public async Task EndorsesAndAbstainsByReferenceAndListsWhatWasSaid()
    {
        ModRef other = new(_site.LinkedMod.Game, "99");

        await _endorsements.EndorseAsync(_site.LinkedMod, Endorsement.Endorsed, "5.2", Cancellation);
        await _endorsements.EndorseAsync(other, Endorsement.Abstained, null, Cancellation);

        Assert.Equal([(_site.LinkedMod, Endorsement.Endorsed, "5.2"), (other, Endorsement.Abstained, (string?)null)], _site.Given);
        IReadOnlyDictionary<ModRef, Endorsement> said = await _endorsements.ListAsync(Endorsing, Cancellation);
        Assert.Equal((Endorsement.Endorsed, Endorsement.Abstained), (said[_site.LinkedMod], said[other]));
    }

    [Fact]
    public async Task EndorsesTheModALinkIsToWithItsSitesVersion()
    {
        ModRef mod = await _endorsements.EndorseAsync(new Uri("https://endorsing.example/mods/35407"), Endorsement.Endorsed, Cancellation);

        Assert.Equal(_site.LinkedMod, mod);
        Assert.Equal([(_site.LinkedMod, Endorsement.Endorsed, (string?)null)], _site.Given);
    }

    [Fact]
    public async Task RefusesASiteThatCantEndorse()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _endorsements.ListAsync(Following, Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _endorsements.EndorseAsync(TestData.File(Following, "1").Mod, Endorsement.Endorsed, null, Cancellation));

        SiteEndorsements followingOnly = new(new SourceRegistry([new FakeWatchlistSource(Following)]));
        UnsupportedLinkException refused = await Assert.ThrowsAsync<UnsupportedLinkException>(
            () => followingOnly.EndorseAsync(new Uri("https://following.example/mods/1"), Endorsement.Endorsed, Cancellation));
        Assert.Equal("This link isn't to a mod on a site where ModShelf can endorse mods.", refused.Message);
    }
}
