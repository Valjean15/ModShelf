namespace ModShelf.Application.Desktop.Ports;

/// <summary>
/// Keeps one desktop app running at a time for a library. A second launch hands its arguments to the one running, which
/// shows its window, and ends. Platform implements it.
/// </summary>
public interface ISingleInstance
{
    /// <summary>
    /// Raised each time another launch hands this one its arguments, on a thread of its own.
    /// </summary>
    event EventHandler<Launch>? LaunchedAgain;

    /// <summary>
    /// Makes this process the desktop app, unless another one is: then hands that one <paramref name="arguments"/>.
    /// </summary>
    /// <remarks>
    /// It's synchronous because the desktop app claims it on its main thread, before anything else starts.
    /// </remarks>
    /// <returns>True when this process is the desktop app now; false when another one is, and has been told.</returns>
    /// <exception cref="TimeoutException">Another desktop app is running, but didn't answer.</exception>
    bool TryClaim(IReadOnlyList<string> arguments);
}
