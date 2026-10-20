using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The shell window's view model. Phase 5 adds navigation between the app's screens.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    public string Title { get; } = Strings.AppTitle;
}
