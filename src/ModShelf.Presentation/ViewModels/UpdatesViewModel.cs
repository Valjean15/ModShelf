using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The updates the checks found. Step 3 fills it in.
/// </summary>
public sealed class UpdatesViewModel : ScreenViewModel
{
    public override Screen Screen => Screen.Updates;

    public override string Title => Strings.UpdatesTitle;
}
