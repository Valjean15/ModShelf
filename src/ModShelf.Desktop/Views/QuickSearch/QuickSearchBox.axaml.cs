using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using ModShelf.Presentation.ViewModels.QuickSearch;

namespace ModShelf.Desktop.Views.QuickSearch;

/// <summary>
/// Quick search's box over the window. As it opens, the keyboard goes to its field; as it closes, back where it was. The
/// row the arrow keys move to scrolls into view, and clicking outside the box closes it.
/// </summary>
public sealed partial class QuickSearchBox : UserControl
{
    private QuickSearchViewModel? _search;
    private IInputElement? _before;

    public QuickSearchBox()
    {
        InitializeComponent();
        Scrim.PointerPressed += (_, e) =>
        {
            _search?.CloseCommand.Execute(null);
            e.Handled = true;
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_search is not null)
        {
            _search.PropertyChanged -= OnSearchChanged;
        }

        _search = DataContext as QuickSearchViewModel;
        if (_search is not null)
        {
            _search.PropertyChanged += OnSearchChanged;
        }
    }

    private void OnSearchChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickSearchViewModel.IsOpen))
        {
            if (_search?.IsOpen is true)
            {
                _before ??= TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                Dispatcher.UIThread.Post(() => Field.Focus(), DispatcherPriority.Input);
            }
            else
            {
                IInputElement? before = _before;
                _before = null;
                if (before is Visual visual && TopLevel.GetTopLevel(visual) is not null && before.Focusable)
                {
                    before.Focus();
                }
            }
        }
        else if (e.PropertyName == nameof(QuickSearchViewModel.Highlighted) && _search?.Highlighted is { } highlighted)
        {
            EntriesList.ContainerFromItem(highlighted)?.BringIntoView();
        }
    }
}
