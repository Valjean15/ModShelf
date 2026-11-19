using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Contents;
using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// A folder or a file in an archive, as a row of its tree: a folder opens and closes to show what's in it (ADR 0106).
/// </summary>
public sealed partial class ContentRowViewModel : ObservableObject
{
    private readonly Action<ContentRowViewModel> _toggled;

    internal ContentRowViewModel(ArchiveEntry entry, Action<ContentRowViewModel> toggled)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(toggled);

        Entry = entry;
        _toggled = toggled;
    }

    internal ArchiveEntry Entry { get; }

    /// <summary>
    /// What's in it, folders first, as the archive lists them; none for a file.
    /// </summary>
    internal List<ContentRowViewModel> Children { get; } = [];

    /// <summary>
    /// Its files' sizes together, for a folder, or its own.
    /// </summary>
    internal long SizeInBytes { get; set; }

    public string Name => Entry.Name;

    public string Path => Entry.Path;

    /// <summary>
    /// How many folders deep it is, which the view indents it by.
    /// </summary>
    public int Depth => Entry.Depth;

    public bool IsFolder => Entry.IsFolder;

    /// <summary>
    /// Whether it's a file shown as text when chosen, such as a readme.
    /// </summary>
    public bool IsText => Entry.IsText;

    public string SizeText => Sizes.Format(SizeInBytes);

    /// <summary>
    /// Whether a folder shows what's in it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; internal set; }

    /// <summary>
    /// Opens or closes a folder.
    /// </summary>
    [RelayCommand]
    private void Toggle()
    {
        if (IsFolder)
        {
            IsExpanded = !IsExpanded;
            _toggled(this);
        }
    }
}
