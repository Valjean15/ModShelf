using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Sources.Abstractions.Tests.Accounts;

public sealed class AuthStartTests
{
    [Theory]
    [InlineData("http://127.0.0.1:49152/callback")]
    [InlineData("http://[::1]:49152/callback")]
    [InlineData("http://localhost:49152/callback")]
    public void AcceptsALoopbackRedirect(string redirectUri)
    {
        Assert.Equal(new Uri(redirectUri), new AuthStart(redirectUri: new Uri(redirectUri)).RedirectUri);
    }

    [Theory]
    [InlineData("https://example.com/callback")]
    [InlineData("http://192.168.1.10:49152/callback")]
    [InlineData("modshelf://callback")]
    public void RejectsARedirectAwayFromThisComputer(string redirectUri)
    {
        Assert.Throws<ArgumentException>(() => new AuthStart(redirectUri: new Uri(redirectUri)));
    }

    [Fact]
    public void KeepsTheEmailAddressTrimmed()
    {
        Assert.Equal("jean@example.com", new AuthStart(email: " jean@example.com ").Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("jean.example.com")]
    public void RejectsSomethingThatIsNotAnEmailAddress(string email)
    {
        Assert.Throws<ArgumentException>(() => new AuthStart(email: email));
    }

    [Fact]
    public void NeedsNothingForAnApiKey()
    {
        AuthStart start = new();

        Assert.Null(start.RedirectUri);
        Assert.Null(start.Email);
    }
}
