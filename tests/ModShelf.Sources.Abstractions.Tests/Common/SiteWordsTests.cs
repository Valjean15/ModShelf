using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Sources.Abstractions.Tests.Common;

public sealed class SiteWordsTests
{
    [Fact]
    public void NoneHasNoWords()
    {
        SiteWords none = SiteWords.None;

        Assert.Equal(
            ((string?)null, (string?)null, (string?)null, (string?)null, (string?)null),
            (none.Follow, none.Unfollow, none.Followed, none.Approval, none.Approvals));
        Assert.Equal((false, false), (none.CanFollow, none.CountsApproval));
    }

    [Fact]
    public void KeepsEachWordTrimmed()
    {
        SiteWords words = new() { Follow = " subscribe ", Unfollow = "unsubscribe\t", Followed = "subscribed", Approval = " like", Approvals = "likes " };

        Assert.Equal(
            ("subscribe", "unsubscribe", "subscribed", "like", "likes"),
            (words.Follow, words.Unfollow, words.Followed, words.Approval, words.Approvals));
        Assert.Equal((true, true), (words.CanFollow, words.CountsApproval));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ABlankWordIsNone(string? blank)
    {
        SiteWords words = new() { Follow = blank, Approvals = blank };

        Assert.Equal(((string?)null, (string?)null), (words.Follow, words.Approvals));
    }

    [Fact]
    public void FollowsOnlyWithAllThreeWords()
    {
        Assert.False(new SiteWords { Follow = "track", Unfollow = "untrack" }.CanFollow);
        Assert.False(new SiteWords { Follow = "track", Followed = "tracked" }.CanFollow);
        Assert.False(new SiteWords { Unfollow = "untrack", Followed = "tracked" }.CanFollow);
    }

    [Fact]
    public void CountsApprovalOnlyWithBothWords()
    {
        Assert.False(new SiteWords { Approval = "endorsement" }.CountsApproval);
        Assert.False(new SiteWords { Approvals = "endorsements" }.CountsApproval);
    }
}
