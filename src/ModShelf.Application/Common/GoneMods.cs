using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Common;

/// <summary>
/// Whether a mod whose files its site won't list is gone from it, hidden or removed there (ADR 0099), as update checks
/// and freeing up space ask it.
/// </summary>
internal static class GoneMods
{
    /// <summary>
    /// Whether a failure to list a mod's files may mean it's gone: its site refused it, or doesn't know it.
    /// </summary>
    public static bool MightBe(Exception exception)
        => exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Forbidden };

    /// <summary>
    /// Whether the site says the mod is gone, asked once: it describes the mod as hidden or removed, or doesn't know it at
    /// all. Anything else, such as a site that can't be reached, isn't taken to mean so.
    /// </summary>
    public static async Task<bool> IsGoneAsync(IModCatalog catalog, ModRef mod, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        try
        {
            ModInfo info = await catalog.GetModAsync(mod, cancellationToken).ConfigureAwait(false);
            return !info.IsAvailable;
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or AuthenticationFailedException or ArgumentException)
        {
            return false;
        }
    }
}
