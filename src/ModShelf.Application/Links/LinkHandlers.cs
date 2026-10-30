using Microsoft.Extensions.Logging;

using ModShelf.Application.Links.Ports;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Links;

/// <summary>
/// Asks the system's register about the schemes of the sources that hand their downloads over with links of their own.
/// </summary>
internal sealed partial class LinkHandlers(ISourceRegistry sources, ISchemeRegistration registration, ILogger<LinkHandlers> logger)
    : ILinkHandlers
{
    public async Task<IReadOnlyList<LinkHandler>> ListAsync(CancellationToken cancellationToken)
    {
        List<LinkHandler> handlers = [];
        foreach (IHandoffScheme source in sources.All<IHandoffScheme>())
        {
            handlers.Add(await DescribeAsync(source, cancellationToken).ConfigureAwait(false));
        }

        return handlers;
    }

    public async Task<LinkHandler> TakeAsync(SourceId source, CancellationToken cancellationToken)
    {
        IHandoffScheme site = Require(source);
        await registration.RegisterAsync(site.Scheme, cancellationToken).ConfigureAwait(false);
        LogTaken(logger, site.Id, site.Scheme);
        return await DescribeAsync(site, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LinkHandler> GiveBackAsync(SourceId source, CancellationToken cancellationToken)
    {
        IHandoffScheme site = Require(source);
        await registration.UnregisterAsync(site.Scheme, cancellationToken).ConfigureAwait(false);
        LogGivenBack(logger, site.Id, site.Scheme);
        return await DescribeAsync(site, cancellationToken).ConfigureAwait(false);
    }

    private IHandoffScheme Require(SourceId source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return sources.Require<IHandoffScheme>(source, "hands nothing over with links of its own", nameof(source));
    }

    private async Task<LinkHandler> DescribeAsync(IHandoffScheme source, CancellationToken cancellationToken)
        => new(source.Id, source.DisplayName, source.Scheme, await registration.GetAsync(source.Scheme, cancellationToken).ConfigureAwait(false));
}
