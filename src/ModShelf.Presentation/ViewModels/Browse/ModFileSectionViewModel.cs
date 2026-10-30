namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// One section of a mod's files, as its site sorts them, such as its main files, newest first.
/// </summary>
public sealed class ModFileSectionViewModel
{
    internal ModFileSectionViewModel(string title, IReadOnlyList<ModFileViewModel> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(files);

        Title = title;
        Files = files;
    }

    /// <summary>
    /// Such as "Main files".
    /// </summary>
    public string Title { get; }

    public IReadOnlyList<ModFileViewModel> Files { get; }
}
