using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// Something that happened in the library, as the Library's recent activity shows it: when, what, and the mod, whose
/// page it opens.
/// </summary>
public sealed partial class ActivityEntryViewModel
{
    private readonly ModRef _mod;
    private readonly Func<ModRef, CancellationToken, Task> _open;

    /// <param name="siteName">The game's site's name while sites show, such as Nexus Mods; null while they don't.</param>
    internal ActivityEntryViewModel(LibraryEvent happened, string gameName, string? siteName, TimeProvider timeProvider, Func<ModRef, CancellationToken, Task> open)
    {
        _mod = happened.Mod;
        _open = open;
        Kind = happened.Kind;
        TimeText = Dates.Time(happened.At, timeProvider);
        KindText = TagOf(happened.Kind);
        ModName = happened.ModName;
        GameName = gameName;
        SiteName = siteName ?? string.Empty;
        Text = Describe(happened);
    }

    public LibraryEventKind Kind { get; }

    /// <summary>
    /// When it happened, such as "4:40 PM".
    /// </summary>
    public string TimeText { get; }

    /// <summary>
    /// What happened in a word, such as "Downloaded".
    /// </summary>
    public string KindText { get; }

    public string ModName { get; }

    public string GameName { get; }

    /// <summary>
    /// The game's site's name, such as Nexus Mods, on its chip; empty while sites don't show.
    /// </summary>
    public string SiteName { get; }

    /// <summary>
    /// Whether the game's site shows, as a chip: once two sites are in use.
    /// </summary>
    public bool ShowSite => SiteName.Length > 0;

    /// <summary>
    /// What happened, such as "Downloaded SkyUI 5.3 (5.3), replacing 5.2."
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Opens the mod's page.
    /// </summary>
    [RelayCommand]
    private Task OpenAsync(CancellationToken cancellationToken) => _open(_mod, cancellationToken);

    private static string TagOf(LibraryEventKind kind) => kind switch
    {
        LibraryEventKind.Downloaded => Strings.ActivityTagDownloaded,
        LibraryEventKind.Archived => Strings.ActivityTagArchived,
        LibraryEventKind.Deleted => Strings.ActivityTagDeleted,
        LibraryEventKind.Found => Strings.ActivityTagFound,
        _ => Strings.ActivityTagRemoved,
    };

    private static string Describe(LibraryEvent happened) => happened.Kind switch
    {
        LibraryEventKind.Downloaded when happened.Replaces is not null
            => Common.Text.Format(Strings.ActivityDownloadedReplacing, FileOf(happened), happened.ReplacesVersion ?? Strings.ActivityAnOlderVersion),
        LibraryEventKind.Downloaded => Common.Text.Format(Strings.ActivityDownloaded, FileOf(happened)),
        LibraryEventKind.Archived => Common.Text.Format(Strings.ActivityArchived, FileOf(happened)),
        LibraryEventKind.Deleted => Common.Text.Format(happened.ByRule ? Strings.ActivityDeletedByRule : Strings.ActivityDeleted, FileOf(happened)),
        LibraryEventKind.Found => Common.Text.Format(Strings.ActivityFound, FileOf(happened)),
        _ => Strings.ActivityRemoved,
    };

    /// <summary>
    /// The file by its name on its site, with its version when that says one.
    /// </summary>
    private static string FileOf(LibraryEvent happened)
    {
        string name = happened.FileName ?? happened.File?.Id ?? string.Empty;
        return happened.VersionLabel is { } version ? Common.Text.Format(Strings.ActivityFileVersion, name, version) : name;
    }
}
