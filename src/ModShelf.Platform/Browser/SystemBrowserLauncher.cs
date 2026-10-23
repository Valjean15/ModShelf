using System.ComponentModel;
using System.Diagnostics;

using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Platform.Browser;

/// <summary>
/// Opens web pages in the default browser: through the shell on Windows, <c>open</c> on macOS and <c>xdg-open</c>
/// on Linux. The URL is passed as a single argument, never through a command line a shell would parse.
/// </summary>
internal sealed class SystemBrowserLauncher : IBrowserLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _start;

    public SystemBrowserLauncher()
        : this(Process.Start)
    {
    }

    /// <param name="start">Starts the process; tests replace it.</param>
    public SystemBrowserLauncher(Func<ProcessStartInfo, Process?> start)
    {
        ArgumentNullException.ThrowIfNull(start);

        _start = start;
    }

    public Task<bool> TryOpenAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Only absolute http and https URLs are opened in the browser.", nameof(url));
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // The shell on Windows may hand the URL to a browser that's already running and return no process.
            using Process? process = _start(StartInfoFor(url));
            return Task.FromResult(true);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // No browser, or no opener such as xdg-open on a server.
            return Task.FromResult(false);
        }
    }

    private static ProcessStartInfo StartInfoFor(Uri url)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true };
        }

        ProcessStartInfo info = new(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(url.AbsoluteUri);
        return info;
    }
}
