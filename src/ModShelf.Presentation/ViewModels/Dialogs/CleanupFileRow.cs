using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Application.Cleanup;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// A file that could go, in the Free up space dialog: chosen to go or not, or kept, saying why.
/// </summary>
public sealed partial class CleanupFileRow : ObservableObject
{
    private readonly Action<CleanupFileRow> _selectionChanged;

    /// <param name="said">What to say beside it: why it's kept, or what it's a copy of; null for nothing.</param>
    /// <param name="selectionChanged">Told when it's chosen or no longer is.</param>
    internal CleanupFileRow(CleanupEntry entry, string? said, bool selected, Action<CleanupFileRow> selectionChanged)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(selectionChanged);

        Entry = entry;
        Said = said;
        SizeText = Sizes.Format(entry.SizeInBytes);
        IsSelected = selected && entry.CanGo;
        _selectionChanged = selectionChanged;
    }

    public CleanupEntry Entry { get; }

    public string ModName => Entry.ModName;

    /// <summary>
    /// Its name on disk.
    /// </summary>
    public string FileName => Entry.FileName;

    /// <summary>
    /// Its mod, and the folder it's in, in its game's folder.
    /// </summary>
    public string Where => Text.Format(Strings.CleanupFileWhere, Entry.ModName, Entry.RelativePath[..Math.Max(0, Entry.RelativePath.Length - Entry.FileName.Length - 1)]);

    /// <summary>
    /// Where it is in its game's folder.
    /// </summary>
    public string Path => Entry.RelativePath;

    /// <summary>
    /// Where it is, in full, for its tooltip.
    /// </summary>
    public string FullPath => Entry.Path;

    public string SizeText { get; }

    /// <summary>
    /// Why it's kept, or what it's a copy of; null for nothing.
    /// </summary>
    public string? Said { get; }

    public bool HasSaid => Said is not null;

    /// <summary>
    /// Whether it can be chosen to go: it isn't kept.
    /// </summary>
    public bool CanGo => Entry.CanGo;

    /// <summary>
    /// Whether it's chosen to go. A kept file never is.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanging(bool value)
    {
        if (value && !CanGo)
        {
            throw new InvalidOperationException($"{Entry.FileName} is kept, so it can't be chosen to go.");
        }
    }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged?.Invoke(this);
}
