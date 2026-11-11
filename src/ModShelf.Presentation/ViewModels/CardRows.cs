using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// A list's cards in rows, as many to a row as <see cref="Columns"/>, so a view builds only the rows it shows rather
/// than every card at once. The rows follow the list as it changes, each keeping the cards that stay in it; changes made
/// in <see cref="Batch"/> are followed once, when it ends.
/// </summary>
public sealed class CardRows
{
    private readonly IList _cards;

    private int _columns = 1;

    private int _batches;

    private CardRows(IList cards, INotifyCollectionChanged changes)
    {
        _cards = cards;
        changes.CollectionChanged += (_, _) => OnCardsChanged();
        Arrange();
    }

    /// <summary>
    /// The rows, each holding the next <see cref="Columns"/> cards of the list, the last one the cards left.
    /// </summary>
    public ObservableCollection<CardRow> Rows { get; } = [];

    /// <summary>
    /// How many cards a row holds, as many as fit across the view: at least one.
    /// </summary>
    public int Columns
    {
        get => _columns;
        set
        {
            _columns = Math.Max(1, value);
            Arrange();
        }
    }

    /// <summary>
    /// The rows of <paramref name="cards"/>, which follow it.
    /// </summary>
    internal static CardRows Of<T>(ObservableCollection<T> cards)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(cards);
        return new CardRows(cards, cards);
    }

    /// <summary>
    /// Makes the changes <paramref name="change"/> makes to the list, then follows them all at once, so a list sorted
    /// again moves each card in the rows once rather than with each of its moves.
    /// </summary>
    internal void Batch(Action change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _batches++;
        try
        {
            change();
        }
        finally
        {
            _batches--;
            if (_batches == 0)
            {
                Arrange();
            }
        }
    }

    private void OnCardsChanged()
    {
        if (_batches == 0)
        {
            Arrange();
        }
    }

    private void Arrange()
    {
        int rows = (_cards.Count + _columns - 1) / _columns;
        for (int index = 0; index < rows; index++)
        {
            int first = index * _columns;
            object[] cards = new object[Math.Min(_columns, _cards.Count - first)];
            for (int at = 0; at < cards.Length; at++)
            {
                cards[at] = _cards[first + at]!;
            }

            if (index < Rows.Count)
            {
                Rows[index].Show(cards, _columns);
            }
            else
            {
                CardRow row = new();
                row.Show(cards, _columns);
                Rows.Add(row);
            }
        }

        while (Rows.Count > rows)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
    }
}
