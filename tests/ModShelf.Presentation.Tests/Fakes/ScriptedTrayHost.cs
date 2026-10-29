using ModShelf.Application.Desktop.Ports;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A tray that's there or not, as the test says, counting how often it was asked.
/// </summary>
internal sealed class ScriptedTrayHost : ITrayHost
{
    public bool IsAvailable { get; set; } = true;

    public int Asked { get; private set; }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        Asked++;
        return Task.FromResult(IsAvailable);
    }
}
