using Avalonia.Controls;

using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // A game's cover is read once its row shows: the list builds only the rows scrolled to.
        GamesList.ContainerPrepared += (_, prepared) =>
        {
            if (GamesList.ItemsView[prepared.Index] is GameRowViewModel row)
            {
                row.ShowCover();
            }
        };
    }
}
