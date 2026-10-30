using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A browser that opens pages or not, as the test says, recording each page.
/// </summary>
internal sealed class ScriptedBrowser : IBrowserLauncher
{
    public List<Uri> Opened { get; } = [];

    public bool CanOpen { get; set; } = true;

    /// <summary>
    /// What opening a page throws, such as the launcher refusing its address; null to open or not as told.
    /// </summary>
    public Exception? Failure { get; set; }

    public Task<bool> TryOpenAsync(Uri url, CancellationToken cancellationToken)
    {
        Opened.Add(url);
        return Failure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(CanOpen);
    }
}
