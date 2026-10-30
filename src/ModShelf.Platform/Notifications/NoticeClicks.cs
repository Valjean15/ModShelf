using Microsoft.Extensions.Logging;

using ModShelf.Application.Links;
using ModShelf.Application.Links.Ports;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// What a click on a Windows notice opens: a link of ModShelf's own scheme, <c>modshelf://</c>, which the system opens
/// with the desktop app on this library, as it opens <c>nxm://</c> links once ModShelf takes them (ADR 0036). No other
/// app opens these links, so ModShelf registers itself for them without asking, before a notice shows.
/// </summary>
/// <remarks>
/// A link that starts the app, or reaches the one running, opens the screen it names, such as
/// <c>modshelf://updates</c>.
/// </remarks>
internal sealed partial class NoticeClicks(ISchemeRegistration links, ILogger logger)
{
    /// <summary>
    /// ModShelf's own scheme.
    /// </summary>
    public const string Scheme = "modshelf";

    /// <summary>
    /// Where a notice of new updates leads: the Updates screen. Every notice is one, so far (ADR 0022).
    /// </summary>
    public const string UpdatesLink = "modshelf://updates";

    /// <summary>
    /// The link a click on a notice opens, once the system opens it with this ModShelf; null when it can't, as when
    /// the desktop app isn't there, and a click then only closes the notice.
    /// </summary>
    public async Task<string?> LinkAsync(CancellationToken cancellationToken)
    {
        try
        {
            SchemeState state = await links.GetAsync(Scheme, cancellationToken).ConfigureAwait(false);
            if (state.Kind == SchemeHandlerKind.Unavailable)
            {
                return null;
            }

            // Another ModShelf's, such as one on another library, which registered before its own notice.
            if (state.Kind != SchemeHandlerKind.ModShelf)
            {
                await links.RegisterAsync(Scheme, cancellationToken).ConfigureAwait(false);
            }

            return UpdatesLink;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            LogCantOpen(logger, exception);
            return null;
        }
    }

    /// <summary>
    /// Stops the system opening the links with ModShelf, as when it's uninstalled.
    /// </summary>
    public Task RemoveAsync(CancellationToken cancellationToken) => links.UnregisterAsync(Scheme, cancellationToken);
}
