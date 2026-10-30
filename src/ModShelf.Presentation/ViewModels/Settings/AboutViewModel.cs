using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Diagnostics;
using ModShelf.Application.Diagnostics.Ports;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// About this ModShelf, at the end of the Settings screen: its version, and what a bug report needs, its log folder and
/// its diagnostics report.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IAppFolders _folders;
    private readonly IDiagnostics _diagnostics;
    private readonly IClipboard _clipboard;
    private readonly IFolderOpener _opener;
    private readonly ILogger<AboutViewModel> _logger;

    public AboutViewModel(
        AppInfo app,
        IAppFolders folders,
        IDiagnostics diagnostics,
        IClipboard clipboard,
        IFolderOpener opener,
        ILogger<AboutViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(opener);
        ArgumentNullException.ThrowIfNull(logger);

        _folders = folders;
        _diagnostics = diagnostics;
        _clipboard = clipboard;
        _opener = opener;
        _logger = logger;
        VersionText = Text.Format(Strings.AboutVersion, app);
    }

    /// <summary>
    /// Such as "ModShelf 0.9.0 (a60d76b)".
    /// </summary>
    public string VersionText { get; }

    /// <summary>
    /// What the last command did, or why it didn't; empty for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    /// <summary>
    /// Whether <see cref="Note"/> says something went wrong.
    /// </summary>
    [ObservableProperty]
    public partial bool IsNoteAProblem { get; private set; }

    /// <summary>
    /// Opens the folder of the log files in the system's file manager, or says where it is when it can't.
    /// </summary>
    [RelayCommand]
    private Task OpenLogFolderAsync(CancellationToken cancellationToken)
        => UserCommands.RunInPlaceAsync(
            async () =>
            {
                bool opened = await _opener.TryOpenAsync(_folders.LogFolder, cancellationToken);
                Tell(opened ? string.Empty : Text.Format(Strings.AboutLogFolderByHand, _folders.LogFolder), isProblem: false);
            },
            _ => null,
            problem => Tell(problem, isProblem: true),
            exception => LogOpenLogFolderFailed(_logger, exception),
            cancellationToken);

    /// <summary>
    /// Puts the diagnostics report on the clipboard, for a bug report.
    /// </summary>
    [RelayCommand]
    private Task CopyDiagnosticsAsync(CancellationToken cancellationToken)
        => UserCommands.RunInPlaceAsync(
            async () =>
            {
                string report = await _diagnostics.DescribeAsync(cancellationToken);
                await _clipboard.SetTextAsync(report, cancellationToken);
                Tell(Strings.AboutDiagnosticsCopied, isProblem: false);
            },
            _ => null,
            problem => Tell(problem, isProblem: true),
            exception => LogCopyDiagnosticsFailed(_logger, exception),
            cancellationToken);

    private void Tell(string note, bool isProblem)
    {
        Note = note;
        IsNoteAProblem = isProblem;
    }
}
