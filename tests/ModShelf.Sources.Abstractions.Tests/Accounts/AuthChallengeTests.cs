using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Sources.Abstractions.Tests.Accounts;

public sealed class AuthChallengeTests
{
    private static readonly Uri AuthorizePage =
        new("https://users.example.com/oauth/authorize?client_id=modshelf&state=st-4b3a&code_challenge=cc-2d1e");

    [Fact]
    public void KeepsWhatTheUserHasToDo()
    {
        AuthChallenge challenge = new(AuthKind.EmailCode, sentTo: "j***@example.com");

        Assert.Equal(AuthKind.EmailCode, challenge.Kind);
        Assert.Null(challenge.Url);
        Assert.Equal("j***@example.com", challenge.SentTo);
        Assert.Empty(challenge.State);
    }

    [Fact]
    public void CopiesTheStrategysState()
    {
        Dictionary<string, string> state = new() { ["code_verifier"] = "cv-6f5e" };

        AuthChallenge challenge = new(AuthKind.OAuthPkce, AuthorizePage, state: state);
        state["code_verifier"] = "changed";

        Assert.Equal("cv-6f5e", challenge.State["code_verifier"]);
    }

    [Fact]
    public void NeverShowsTheStateOrTheUrlsQuery()
    {
        string text = new AuthChallenge(
            AuthKind.OAuthPkce,
            AuthorizePage,
            state: new Dictionary<string, string> { ["code_verifier"] = "cv-6f5e" }).ToString();

        Assert.DoesNotContain("cv-6f5e", text, StringComparison.Ordinal);
        Assert.DoesNotContain("st-4b3a", text, StringComparison.Ordinal);
        Assert.Contains("users.example.com/oauth/authorize", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    public void RejectsAUrlThatMustNotBeOpened(string url)
    {
        Assert.Throws<ArgumentException>(() => new AuthChallenge(AuthKind.ApiKey, new Uri(url)));
    }

    [Fact]
    public void RejectsAnUnknownKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthChallenge((AuthKind)99));
    }
}
