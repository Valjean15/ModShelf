using ModShelf.Application.Diagnostics;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Settings;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// About this ModShelf, on the Settings screen: its version, its log folder and its diagnostics report.
/// </summary>
public sealed class AboutViewModelTests
{
    private readonly FixedDiagnostics _diagnostics = new();
    private readonly RecordingClipboard _clipboard = new();
    private readonly ScriptedFolders _opener = new();
    private readonly RecordingLogger<AboutViewModel> _logger = new();

    [Fact]
    public void ShowsTheVersionAndCommit()
    {
        Assert.Equal("ModShelf 0.9.0 (a60d76b)", About(new AppInfo("0.9.0", "a60d76b")).VersionText);
        Assert.Equal("ModShelf 0.9.1-alpha.0.3", About(new AppInfo("0.9.1-alpha.0.3", null)).VersionText);
    }

    [Fact]
    public async Task CopiesTheDiagnosticsReportAndSaysSo()
    {
        AboutViewModel about = About();

        await about.CopyDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal([_diagnostics.Report], _clipboard.Texts);
        Assert.Equal(("The diagnostics are on the clipboard. Paste them into your bug report.", false), (about.Note, about.IsNoteAProblem));
    }

    [Fact]
    public async Task AClipboardThatRefusesIsLoggedAndTold()
    {
        AboutViewModel about = About();
        _clipboard.Refusal = new InvalidOperationException("The main window isn't open.");

        await about.CopyDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal(("Something went wrong. The log has the details.", true), (about.Note, about.IsNoteAProblem));
        Assert.StartsWith("Error: Copying the diagnostics report failed", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpensTheLogFolder()
    {
        AboutViewModel about = About();

        await about.OpenLogFolderCommand.ExecuteAsync(null);

        Assert.Equal([_diagnostics.LogFolder], _opener.Opened);
        Assert.Equal(string.Empty, about.Note);
    }

    [Fact]
    public async Task ALogFolderThatDoesntOpenIsNamed()
    {
        AboutViewModel about = About();
        _opener.CanOpen = false;

        await about.OpenLogFolderCommand.ExecuteAsync(null);

        Assert.Equal(($"The log folder didn't open. It's {_diagnostics.LogFolder}.", false), (about.Note, about.IsNoteAProblem));
    }

    [Fact]
    public async Task ANoteGoesOnceTheNextCommandWorks()
    {
        AboutViewModel about = About();
        _opener.CanOpen = false;
        await about.OpenLogFolderCommand.ExecuteAsync(null);
        _opener.CanOpen = true;

        await about.OpenLogFolderCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, about.Note);
    }

    private AboutViewModel About(AppInfo? app = null)
        => new(app ?? new AppInfo("0.9.0", "a60d76b"), _diagnostics, _diagnostics, _clipboard, _opener, _logger);
}
