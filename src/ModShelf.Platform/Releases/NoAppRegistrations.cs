using ModShelf.Application.Releases.Ports;

namespace ModShelf.Platform.Releases;

/// <summary>
/// macOS and Linux keep nothing for ModShelf itself: its notices need no registration, and their clicks open nothing.
/// </summary>
internal sealed class NoAppRegistrations : IAppRegistrations
{
    public Task RemoveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
