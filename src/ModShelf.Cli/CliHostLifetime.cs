using Microsoft.Extensions.Hosting;

namespace ModShelf.Cli;

/// <summary>
/// Leaves Ctrl+C to System.CommandLine, which cancels the running command, instead of the default console
/// lifetime, which would also react to it and print its own startup messages.
/// </summary>
internal sealed class CliHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
