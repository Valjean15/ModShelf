using ModShelf.Application.Links;
using ModShelf.Application.Links.Ports;

namespace ModShelf.Platform.Links;

/// <summary>
/// The register of a system ModShelf can't register link schemes on.
/// </summary>
internal sealed class UnavailableSchemeRegistration : ISchemeRegistration
{
    public Task<SchemeState> GetAsync(string scheme, CancellationToken cancellationToken) => Task.FromResult(SchemeState.Unavailable);

    public Task RegisterAsync(string scheme, CancellationToken cancellationToken)
        => throw new InvalidOperationException("ModShelf can't open links on this system.");

    public Task UnregisterAsync(string scheme, CancellationToken cancellationToken) => Task.CompletedTask;
}
