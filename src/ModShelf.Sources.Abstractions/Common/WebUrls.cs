namespace ModShelf.Sources.Abstractions.Common;

internal static class WebUrls
{
    /// <summary>
    /// Whether <paramref name="url"/> is an absolute http or https URL. Anything else (relative paths,
    /// <c>file:</c>, <c>javascript:</c>, custom schemes) must never be fetched or opened in a browser.
    /// </summary>
    public static bool IsHttpOrHttps(Uri url)
        => url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp);
}
