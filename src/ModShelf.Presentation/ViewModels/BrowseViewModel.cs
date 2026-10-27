using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// Searching a source's mods. Step 4 fills it in.
/// </summary>
public sealed class BrowseViewModel : ScreenViewModel
{
    public override Screen Screen => Screen.Browse;

    public override string Title => Strings.BrowseTitle;
}
