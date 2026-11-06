using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// The MCP server's details, each to copy, until closed.
/// </summary>
public sealed partial class AgentDetailsDialog : Window
{
    public AgentDetailsDialog()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(true);
}
