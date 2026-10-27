using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The mods ModShelf has files of or watches, by game. Step 3 fills it in.
/// </summary>
public sealed class LibraryViewModel : ScreenViewModel
{
    public override Screen Screen => Screen.Library;

    public override string Title => Strings.LibraryTitle;
}
