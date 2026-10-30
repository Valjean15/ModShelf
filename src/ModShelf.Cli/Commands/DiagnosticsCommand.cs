using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Diagnostics;
using ModShelf.Cli.Hosting;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>diagnostics</c>: writes the report a bug report needs, as the desktop app's Copy diagnostics does.
/// </summary>
internal static class DiagnosticsCommand
{
    public static Command Create(CliEnvironment environment)
    {
        Command diagnostics = new(
            "diagnostics",
            "Writes a report of this ModShelf for a bug report: its version and system, its folders, the settings, the library, the queue and the linked accounts. It holds no keys, account names or links.");
        diagnostics.SetAction((_, cancellationToken) => WriteAsync(environment, cancellationToken));
        return diagnostics;
    }

    private static Task<int> WriteAsync(CliEnvironment environment, CancellationToken cancellationToken)
        => CommandHost.RunAsync(
            environment,
            async services =>
            {
                string report = await services.GetRequiredService<IDiagnostics>().DescribeAsync(cancellationToken).ConfigureAwait(false);
                await environment.Output.WriteAsync(report).ConfigureAwait(false);
                return ExitCodes.Done;
            },
            cancellationToken);
}
