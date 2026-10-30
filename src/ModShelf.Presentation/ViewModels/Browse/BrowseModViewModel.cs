using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// One mod a search found, or one the account tracks: its name, author and summary, what the site counts, and what the
/// library has of it. Opening it shows its files.
/// </summary>
public sealed partial class BrowseModViewModel : ObservableObject
{
    private readonly Func<ModRef, CancellationToken, Task> _open;

    /// <param name="problem">Why the mod's details couldn't be read, for a tracked mod; null when they were.</param>
    /// <param name="open">Opens the mod on the screen.</param>
    internal BrowseModViewModel(ModInfo mod, string? problem, LibraryMod? inLibrary, TimeProvider timeProvider, Func<ModRef, CancellationToken, Task> open)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(open);

        _open = open;
        Mod = mod.Mod;
        Name = mod.Name;
        ModInfoText text = ModInfoText.Of(mod, timeProvider);
        AuthorText = text.Author;
        Summary = text.Summary;
        VersionText = text.Version;
        UpdatedText = text.Updated;
        DownloadsText = text.Downloads;
        EndorsementsText = text.Endorsements;
        HasAdultContent = mod.HasAdultContent;
        IsHidden = !mod.IsAvailable;
        Problem = problem;
        ShowLibrary(inLibrary);
    }

    public ModRef Mod { get; }

    public string Name { get; }

    /// <summary>
    /// Such as "by schlangster"; empty when the site names no author.
    /// </summary>
    public string AuthorText { get; }

    /// <summary>
    /// A line or two about the mod; empty when the site gives none.
    /// </summary>
    public string Summary { get; }

    /// <summary>
    /// Such as "Version 5.2SE"; empty when the site gives none.
    /// </summary>
    public string VersionText { get; }

    /// <summary>
    /// Such as "Updated 10/27/2026", in the user's time zone and culture; empty when the site doesn't say.
    /// </summary>
    public string UpdatedText { get; }

    /// <summary>
    /// Such as "2,134,567 downloads"; empty when the site doesn't count them.
    /// </summary>
    public string DownloadsText { get; }

    /// <summary>
    /// Such as "98,765 endorsements"; empty when the site doesn't count them.
    /// </summary>
    public string EndorsementsText { get; }

    public bool HasAdultContent { get; }

    /// <summary>
    /// Whether the mod is hidden or removed on its site, so its files can't be downloaded.
    /// </summary>
    public bool IsHidden { get; }

    /// <summary>
    /// Why its details couldn't be read; null when they were.
    /// </summary>
    public string? Problem { get; }

    /// <summary>
    /// Whether the library has files of it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDownloaded { get; private set; }

    /// <summary>
    /// Whether the library watches it for updates.
    /// </summary>
    [ObservableProperty]
    public partial bool IsWatched { get; private set; }

    /// <summary>
    /// Shows what the library has of the mod now; null when nothing. Runs on the UI thread.
    /// </summary>
    internal void ShowLibrary(LibraryMod? inLibrary)
    {
        IsDownloaded = inLibrary?.Files.Count > 0;
        IsWatched = inLibrary?.IsWatched == true;
    }

    /// <summary>
    /// Shows the mod's files and changelog, read from its site.
    /// </summary>
    [RelayCommand]
    private Task OpenAsync(CancellationToken cancellationToken) => _open(Mod, cancellationToken);
}
