using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ModShelf.Desktop.Views.ModPages;

/// <summary>
/// A mod's page, as Library and Browse show it. A mod opened from another's page shows from its top.
/// </summary>
public sealed partial class ModPageView : UserControl
{
    public ModPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => this.FindAncestorOfType<ScrollViewer>()?.ScrollToHome();
    }
}
