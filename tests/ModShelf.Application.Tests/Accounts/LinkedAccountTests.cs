using ModShelf.Application.Accounts;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Tests.Accounts;

public sealed class LinkedAccountTests
{
    private static readonly AccountProfile Jean = new("Jean", tier: "Premium");

    [Fact]
    public void KeepsWhoAndHow()
    {
        LinkedAccount account = new(TestData.Nexus, AuthKind.OAuthPkce, Jean, TestData.Now);

        Assert.Equal(TestData.Nexus, account.Source);
        Assert.Equal(AuthKind.OAuthPkce, account.Kind);
        Assert.Same(Jean, account.Profile);
        Assert.Equal(TestData.Now, account.LinkedAt);
    }

    [Fact]
    public void RejectsAnUnknownKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinkedAccount(TestData.Nexus, (AuthKind)99, Jean, TestData.Now));
    }

    [Fact]
    public void RequiresASourceAndAProfile()
    {
        Assert.Throws<ArgumentNullException>(() => new LinkedAccount(null!, AuthKind.ApiKey, Jean, TestData.Now));
        Assert.Throws<ArgumentNullException>(() => new LinkedAccount(TestData.Nexus, AuthKind.ApiKey, null!, TestData.Now));
    }
}
