using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The download queue. Step 2 fills it in.
/// </summary>
public sealed class DownloadsViewModel : ScreenViewModel
{
    public override Screen Screen => Screen.Downloads;

    public override string Title => Strings.DownloadsTitle;
}
