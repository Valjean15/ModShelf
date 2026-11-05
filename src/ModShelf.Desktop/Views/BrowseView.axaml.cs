using Avalonia.Controls;
using Avalonia.Threading;

namespace ModShelf.Desktop.Views;

public sealed partial class BrowseView : UserControl
{
    public BrowseView()
    {
        InitializeComponent();

        // The focus goes into the filter card as it opens, so its choices and Escape work from the keyboard at once.
        FilterCard.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && change.NewValue is true)
            {
                Dispatcher.UIThread.Post(() => FirstFilter.Focus());
            }
        };
    }
}
