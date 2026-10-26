using System.Runtime.CompilerServices;

using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Sources.Abstractions.Catalog;

/// <summary>
/// The rule for the links a catalog hands out: the app may open them in a browser, so only web links are allowed.
/// </summary>
internal static class CatalogUrls
{
    /// <exception cref="ArgumentException"><paramref name="url"/> isn't null or an absolute HTTP or HTTPS URL.</exception>
    public static void EnsureWebUrl(Uri? url, [CallerArgumentExpression(nameof(url))] string? paramName = null)
    {
        if (url is not null && !WebUrls.IsHttpOrHttps(url))
        {
            throw new ArgumentException("A catalog's links must be absolute HTTP or HTTPS URLs.", paramName);
        }
    }
}
