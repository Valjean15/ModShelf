using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Diagnostics;

namespace ModShelf.Platform.Diagnostics;

/// <summary>
/// Says, as the host starts and before the library database is opened, which ModShelf this is, what it runs, its process
/// ID and the process that started it: several processes may use one library, such as the desktop app and the MCP
/// servers agents' apps start, and their logs then tell them apart (ADR 0081).
/// </summary>
internal sealed partial class ProcessStartLog(string runs, AppInfo app, ILogger logger) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return Task.CompletedTask;
        }

        string version = app.ToString();
        int? parent = ParentProcess.FindId();
        if (parent is { } id)
        {
            string name = ParentProcess.NameOf(id) ?? "a process that has ended";
            LogStarted(logger, version, runs, Environment.ProcessId, name, id);
        }
        else
        {
            LogStartedByUnknown(logger, version, runs, Environment.ProcessId);
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
