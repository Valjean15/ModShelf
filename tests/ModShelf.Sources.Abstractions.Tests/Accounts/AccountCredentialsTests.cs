using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Sources.Abstractions.Tests.Accounts;

public sealed class AccountCredentialsTests
{
    private static readonly DateTimeOffset ExpiresAt = new(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void KeepsTheTokensAndExpiry()
    {
        AccountCredentials credentials = new("access-0f9e", "refresh-8d7c", ExpiresAt);

        Assert.Equal("access-0f9e", credentials.AccessToken);
        Assert.Equal("refresh-8d7c", credentials.RefreshToken);
        Assert.Equal(ExpiresAt, credentials.ExpiresAt);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TreatsABlankRefreshTokenAsNone(string? refreshToken)
    {
        Assert.Null(new AccountCredentials("api-key", refreshToken).RefreshToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequiresAnAccessToken(string accessToken)
    {
        Assert.Throws<ArgumentException>(() => new AccountCredentials(accessToken));
    }

    [Fact]
    public void NeverShowsTheTokens()
    {
        string text = new AccountCredentials("access-0f9e", "refresh-8d7c", ExpiresAt).ToString();

        Assert.DoesNotContain("access-0f9e", text, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-8d7c", text, StringComparison.Ordinal);
        Assert.Contains("2026-10-23", text, StringComparison.Ordinal);
    }
}
