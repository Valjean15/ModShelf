using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Application.Cleanup;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// The heading of a group of files that could go, in the Free up space dialog, with a box that chooses all of them that
/// can go, or none.
/// </summary>
public sealed partial class CleanupGroupRow : ObservableObject
{
    private readonly List<CleanupFileRow> _files = [];

    /// <param name="heading">The group's name, such as "Archived old versions".</param>
    /// <param name="note">What's in the group, in a sentence.</param>
    internal CleanupGroupRow(CleanupGroup group, string heading, string note)
    {
        Group = group;
        Heading = heading;
        Note = note;
    }

    public CleanupGroup Group { get; }

    public string Heading { get; }

    public string Note { get; }

    /// <summary>
    /// How many of its files can go, their size, and how many are kept.
    /// </summary>
    [ObservableProperty]
    public partial string TotalText { get; internal set; } = string.Empty;

    /// <summary>
    /// Its files, in the order they're shown.
    /// </summary>
    public IReadOnlyList<CleanupFileRow> Files => _files;

    /// <summary>
    /// Whether any of its files can go, so the box can choose them.
    /// </summary>
    public bool CanSelect => _files.Any(file => file.CanGo);

    /// <summary>
    /// Whether all of its files that can go are chosen: true when all are, false when none is, null when some are.
    /// Setting it chooses them all, or none.
    /// </summary>
    public bool? IsSelected
    {
        get
        {
            int selected = _files.Count(file => file.IsSelected);
            return selected == 0 ? false : selected == _files.Count(file => file.CanGo) ? true : null;
        }

        set
        {
            // A box clicked never sets null, which would choose them all.
            bool choose = value ?? true;
            foreach (CleanupFileRow file in _files.Where(file => file.CanGo))
            {
                file.IsSelected = choose;
            }

            OnPropertyChanged();
        }
    }

    internal void Add(CleanupFileRow file) => _files.Add(file);

    /// <summary>
    /// Says the files' choice changed.
    /// </summary>
    internal void SelectionChanged() => OnPropertyChanged(nameof(IsSelected));
}
