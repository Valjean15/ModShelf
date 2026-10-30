using Microsoft.Extensions.Hosting;

using ModShelf.Cli.Hosting;

namespace ModShelf.Cli.Commands;

/// <summary>
/// Runs a command's work in a host of its own, without background downloads: starting it only creates or migrates the
/// database. The host stops however the work ends. What the work leaves unhandled, Ctrl+C among it, is
/// <see cref="GuardedAction"/>'s to say.
/// </summary>
internal static class CommandHost
{
    /// <param name="work">The command's work, with the host's services; it gives the exit code.</param>
    public static async Task<int> RunAsync(CliEnvironment environment, Func<IServiceProvider, Task<int>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(work);

        using IHost host = environment.BuildHost(false);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            return await work(host.Services).ConfigureAwait(false);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
