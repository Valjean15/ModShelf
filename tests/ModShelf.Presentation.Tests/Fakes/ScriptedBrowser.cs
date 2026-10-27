using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A browser that opens pages or not, as the test says, recording each page.
/// </summary>
internal sealed class ScriptedBrowser : IBrowserLauncher
{
    public List<Uri> Opened { get; } = [];

    public bool CanOpen { get; set; } = true;

    public Task<bool> TryOpenAsync(Uri url, CancellationToken cancellationToken)
    {
        Opened.Add(url);
        return Task.FromResult(CanOpen);
    }
}
