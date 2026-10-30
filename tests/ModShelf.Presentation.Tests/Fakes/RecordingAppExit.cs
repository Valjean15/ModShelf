using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Counts the times the app was told to quit.
/// </summary>
internal sealed class RecordingAppExit : IAppExit
{
    public int Quits { get; private set; }

    public void Quit() => Quits++;
}
