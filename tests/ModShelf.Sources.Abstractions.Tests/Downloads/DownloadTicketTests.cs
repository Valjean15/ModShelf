using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Sources.Abstractions.Tests.Downloads;

public sealed class DownloadTicketTests
{
    private static readonly Uri SignedUrl =
        new("https://cdn.example.com/files/SkyUI_5_2_SE.7z?signature=secret&expires=1792600000");

    [Fact]
    public void DefaultsToNoHeadersHashOrExpiry()
    {
        DownloadTicket ticket = new(SignedUrl);

        Assert.Same(SignedUrl, ticket.Url);
        Assert.Empty(ticket.Headers);
        Assert.Null(ticket.ExpectedHash);
        Assert.Null(ticket.ExpiresAt);
    }

    [Fact]
    public void KeepsTheExpectedHashAndExpiry()
    {
        FileHash hash = new(FileHashAlgorithm.Md5, "0123456789abcdef0123456789abcdef");
        DateTimeOffset expiresAt = new(2026, 10, 21, 13, 0, 0, TimeSpan.Zero);

        DownloadTicket ticket = new(SignedUrl, expectedHash: hash, expiresAt: expiresAt);

        Assert.Equal(hash, ticket.ExpectedHash);
        Assert.Equal(expiresAt, ticket.ExpiresAt);
    }

    [Theory]
    [InlineData("http://localhost:5000/files/SkyUI_5_2_SE.7z")]
    [InlineData("https://cdn.example.com/files/SkyUI_5_2_SE.7z")]
    public void AcceptsAbsoluteHttpAndHttpsUrls(string url)
    {
        Assert.Equal(new Uri(url), new DownloadTicket(new Uri(url)).Url);
    }

    [Theory]
    [InlineData("ftp://example.com/files/SkyUI_5_2_SE.7z")]
    [InlineData("file:///C:/Mods/SkyUI_5_2_SE.7z")]
    public void RejectsOtherSchemes(string url)
    {
        Assert.Throws<ArgumentException>(() => new DownloadTicket(new Uri(url)));
    }

    [Fact]
    public void RejectsARelativeUrl()
    {
        Assert.Throws<ArgumentException>(() => new DownloadTicket(new Uri("files/SkyUI_5_2_SE.7z", UriKind.Relative)));
    }

    [Fact]
    public void HeaderNamesAreCaseInsensitive()
    {
        DownloadTicket ticket = new(SignedUrl, new Dictionary<string, string> { ["Authorization"] = "Bearer token" });

        Assert.Equal("Bearer token", ticket.Headers["authorization"]);
    }

    [Fact]
    public void KeepsItsOwnCopyOfTheHeaders()
    {
        Dictionary<string, string> headers = new() { ["Authorization"] = "Bearer token" };
        DownloadTicket ticket = new(SignedUrl, headers);

        headers.Clear();

        Assert.Single(ticket.Headers);
    }

    [Fact]
    public void ToStringLeavesOutTheUrlAndHeaders()
    {
        DownloadTicket ticket = new(SignedUrl, new Dictionary<string, string> { ["Authorization"] = "Bearer token" });

        string text = ticket.ToString();

        Assert.Contains("cdn.example.com", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SkyUI", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", text, StringComparison.Ordinal);
    }
}
