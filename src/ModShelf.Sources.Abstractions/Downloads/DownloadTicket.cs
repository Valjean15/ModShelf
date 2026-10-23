using System.Collections.ObjectModel;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Sources.Abstractions.Downloads;

/// <summary>
/// A short-lived permission to download a file: where to fetch it, which headers to send, and how to
/// check the result.
/// </summary>
/// <remarks>
/// The URL is often signed and works only for a limited time. Never store, cache or log a ticket;
/// <see cref="ToString"/> leaves out the URL and headers for that reason.
/// </remarks>
public sealed record DownloadTicket : ResolveResult
{
    public DownloadTicket(
        Uri url,
        IReadOnlyDictionary<string, string>? headers = null,
        FileHash? expectedHash = null,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!WebUrls.IsHttpOrHttps(url))
        {
            throw new ArgumentException("A download URL must be an absolute HTTP or HTTPS URL.", nameof(url));
        }

        Url = url;
        Headers = headers is null
            ? ReadOnlyDictionary<string, string>.Empty
            : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase).AsReadOnly();
        ExpectedHash = expectedHash;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Where to download the file from. Secret: never store, cache or log it.
    /// </summary>
    public Uri Url { get; }

    /// <summary>
    /// Extra request headers, such as authorization. Names compare case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    /// The hash to check the downloaded file against, when the source publishes one.
    /// </summary>
    public FileHash? ExpectedHash { get; }

    /// <summary>
    /// When the URL stops working, when the source says so.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; }

    public override string ToString()
        => $"DownloadTicket {{ Host = {Url.Host}, ExpectedHash = {ExpectedHash}, ExpiresAt = {ExpiresAt:O} }}";
}
