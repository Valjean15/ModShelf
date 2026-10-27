using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The settings. Step 5 fills it in.
/// </summary>
public sealed class SettingsViewModel : ScreenViewModel
{
    public override Screen Screen => Screen.Settings;

    public override string Title => Strings.SettingsTitle;
}
