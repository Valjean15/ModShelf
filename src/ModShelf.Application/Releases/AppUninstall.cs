using Microsoft.Extensions.Logging;

using ModShelf.Application.Links;
using ModShelf.Application.Releases.Ports;

namespace ModShelf.Application.Releases;

/// <summary>
/// Gives the links back and removes ModShelf's own registrations, through the same services the Settings screen and
/// the CLI use, so that the uninstaller puts back the app that had the links before.
/// </summary>
/// <remarks>
/// It runs as the uninstaller starts the app, with no arguments, so it's ModShelf on the default library. A site's
/// links that ModShelf on another library took, such as a checklist's, show as another ModShelf's and stay; the
/// checklists give them back themselves.
/// </remarks>
internal sealed partial class AppUninstall(ILinkHandlers links, IAppRegistrations registrations, ILogger<AppUninstall> logger)
    : IAppUninstall
{
    public async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LinkHandler> handlers = [];
        try
        {
            handlers = await links.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCantList(logger, exception);
        }

        foreach (LinkHandler handler in handlers.Where(handler => handler.State.Kind == SchemeHandlerKind.ModShelf || handler.State.IsModShelfBehind))
        {
            try
            {
                await links.GiveBackAsync(handler.Source, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogCantGiveBack(logger, exception, handler.Source, handler.Scheme);
            }
        }

        try
        {
            await registrations.RemoveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCantRemove(logger, exception);
        }
    }
}
