using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Sources.Abstractions.Tests.Accounts;

public sealed class AccountProfileTests
{
    [Fact]
    public void KeepsTheNameAvatarAndTier()
    {
        Uri avatar = new("https://avatars.example.com/u/42.png");

        AccountProfile profile = new("Jean", avatar, "Premium");

        Assert.Equal("Jean", profile.DisplayName);
        Assert.Same(avatar, profile.AvatarUrl);
        Assert.Equal("Premium", profile.Tier);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TreatsABlankTierAsNone(string? tier)
    {
        Assert.Null(new AccountProfile("Jean", tier: tier).Tier);
    }

    [Fact]
    public void RequiresADisplayName()
    {
        Assert.Throws<ArgumentException>(() => new AccountProfile(" "));
    }

    [Fact]
    public void RejectsAnAvatarThatIsNotAWebAddress()
    {
        Assert.Throws<ArgumentException>(() => new AccountProfile("Jean", new Uri("file:///etc/passwd")));
    }
}
