using Microsoft.Extensions.Hosting;

namespace ModShelf.Desktop.Hosting;

/// <summary>
/// Replaces the default console lifetime. Avalonia owns the process lifetime, so the host
/// must not intercept Ctrl+C or SIGTERM and stop its services while the window stays open.
/// </summary>
internal sealed class DesktopHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
