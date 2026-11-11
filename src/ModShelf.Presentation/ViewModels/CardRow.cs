using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// A row of cards in <see cref="CardRows"/>: the cards, and how many a row holds, so a row with fewer still gives each
/// card its column's width.
/// </summary>
public sealed partial class CardRow : ObservableObject
{
    internal CardRow()
    {
    }

    /// <summary>
    /// The row's cards, in the list's order: the view models the view shows as cards.
    /// </summary>
    public ObservableCollection<object> Cards { get; } = [];

    /// <summary>
    /// How many cards a row holds.
    /// </summary>
    [ObservableProperty]
    public partial int Columns { get; private set; }

    /// <summary>
    /// Makes the row hold <paramref name="cards"/>, keeping the ones it has, out of rows of <paramref name="columns"/>.
    /// </summary>
    internal void Show(IReadOnlyList<object> cards, int columns)
    {
        Columns = columns;
        Cards.SyncWith(cards);
    }
}
