using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Application.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Services;

/// <summary>
/// Finds the sites in use from the library's mods and the linked accounts.
/// </summary>
internal sealed partial class SitesInUse(IModLibrary library, IAccountService accounts, ILogger<SitesInUse> logger) : ISitesInUse
{
    public async Task<bool> AreSeveralAsync(CancellationToken cancellationToken)
    {
        try
        {
            HashSet<SourceId> sites = [.. (await library.ListAsync(cancellationToken)).Select(mod => mod.Mod.Game.Source)];
            if (sites.Count < 2)
            {
                sites.UnionWith((await accounts.ListAsync(cancellationToken)).Where(site => site.Account is not null).Select(site => site.Source));
            }

            return sites.Count > 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogUnknown(logger, exception);
            return false;
        }
    }
}
