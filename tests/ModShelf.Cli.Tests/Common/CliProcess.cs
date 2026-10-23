using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ModShelf.Cli.Tests.Common;

/// <summary>
/// The ModShelf CLI built next to these tests, running as its own process the way a user runs it, with the
/// library database at a path of the test's choosing.
/// </summary>
internal sealed class CliProcess : IDisposable
{
    private static readonly string CliAssembly = Path.Combine(AppContext.BaseDirectory, "ModShelf.Cli.dll");

    private readonly Process _process;
    private readonly Task<string> _output;
    private readonly Task<string> _errors;

    private CliProcess(Process process)
    {
        _process = process;

        // Read both streams from the start, so a full pipe never blocks the CLI.
        _output = process.StandardOutput.ReadToEndAsync();
        _errors = process.StandardError.ReadToEndAsync();
    }

    public bool HasExited => _process.HasExited;

    public static CliProcess Start(string databasePath, IEnumerable<string> arguments)
    {
        ProcessStartInfo start = new(DotnetHost())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(CliAssembly);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["ModShelf__DatabasePath"] = databasePath;
        return new CliProcess(Process.Start(start) ?? throw new InvalidOperationException("The CLI didn't start."));
    }

    /// <summary>
    /// Sends SIGINT, as Ctrl+C in a terminal does. Linux and macOS only: Windows can't send Ctrl+C to a process
    /// that doesn't share the sender's console.
    /// </summary>
    public async Task InterruptAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Only Linux and macOS can send Ctrl+C to another process.");
        }

        using Process kill = Process.Start("kill", ["-INT", _process.Id.ToString(CultureInfo.InvariantCulture)]);
        await kill.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (kill.ExitCode != 0)
        {
            throw new InvalidOperationException($"kill -INT ended with exit code {kill.ExitCode}.");
        }
    }

    /// <summary>
    /// Ends the CLI at once, without letting it clean up, as <c>kill -9</c> or Task Manager's End task does.
    /// </summary>
    public void Kill() => _process.Kill(entireProcessTree: true);

    public async Task<CliResult> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        return new CliResult(
            _process.ExitCode,
            await _output.ConfigureAwait(false),
            await _errors.ConfigureAwait(false));
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        _process.Dispose();
    }

    /// <summary>
    /// The dotnet that runs these tests, so the CLI runs on the same runtime. The shared runtime lives in
    /// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;/</c>, next to the host in <c>&lt;root&gt;</c>.
    /// </summary>
    private static string DotnetHost()
    {
        string root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        string host = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(host) ? host : "dotnet";
    }
}
