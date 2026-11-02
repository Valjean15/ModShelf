using CommunityToolkit.Mvvm.ComponentModel;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// One of the Downloads screen's tabs, with how many downloads it shows.
/// </summary>
public sealed partial class DownloadTabViewModel : ObservableObject
{
    internal DownloadTabViewModel(DownloadTab tab, string label)
    {
        Tab = tab;
        Label = label;
    }

    public DownloadTab Tab { get; }

    /// <summary>
    /// Such as "Waiting for you".
    /// </summary>
    public string Label { get; }

    [ObservableProperty]
    public partial int Count { get; internal set; }
}
