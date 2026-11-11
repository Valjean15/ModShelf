using System.Collections.ObjectModel;
using System.Collections.Specialized;

using ModShelf.Presentation.ViewModels;

namespace ModShelf.Presentation.Tests.ViewModels;

public sealed class CardRowsTests
{
    private readonly ObservableCollection<Card> _cards = [new("a"), new("b"), new("c"), new("d"), new("e")];

    [Fact]
    public void PutsTheCardsInRowsOfAsManyAsTheColumns()
    {
        CardRows rows = CardRows.Of(_cards);

        rows.Columns = 2;

        Assert.Equal(["a b", "c d", "e"], Lines(rows));
        Assert.All(rows.Rows, row => Assert.Equal(2, row.Columns));
    }

    [Fact]
    public void HoldsOneCardToARowUntilTold()
    {
        CardRows rows = CardRows.Of(_cards);

        Assert.Equal(1, rows.Columns);
        Assert.Equal(["a", "b", "c", "d", "e"], Lines(rows));
        Assert.All(rows.Rows, row => Assert.Equal(1, row.Columns));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void HoldsAtLeastOneCardToARow(int columns)
    {
        CardRows rows = CardRows.Of(_cards);
        rows.Columns = 3;

        rows.Columns = columns;

        Assert.Equal(1, rows.Columns);
        Assert.Equal(["a", "b", "c", "d", "e"], Lines(rows));
    }

    [Fact]
    public void ArrangesTheCardsAgainWhenTheColumnsChangeKeepingTheRowsThatStay()
    {
        CardRows rows = CardRows.Of(_cards);
        rows.Columns = 2;
        CardRow first = rows.Rows[0];
        CardRow second = rows.Rows[1];

        rows.Columns = 3;

        Assert.Equal(["a b c", "d e"], Lines(rows));
        Assert.Same(first, rows.Rows[0]);
        Assert.Same(second, rows.Rows[1]);
        Assert.All(rows.Rows, row => Assert.Equal(3, row.Columns));
    }

    [Fact]
    public void FollowsCardsAddedInsertedRemovedAndMoved()
    {
        CardRows rows = CardRows.Of(_cards);
        rows.Columns = 2;

        _cards.Add(new Card("f"));
        Assert.Equal(["a b", "c d", "e f"], Lines(rows));

        _cards.Add(new Card("g"));
        Assert.Equal(["a b", "c d", "e f", "g"], Lines(rows));

        _cards.Insert(0, new Card("z"));
        Assert.Equal(["z a", "b c", "d e", "f g"], Lines(rows));

        _cards.RemoveAt(1);
        Assert.Equal(["z b", "c d", "e f", "g"], Lines(rows));

        _cards.Move(6, 0);
        Assert.Equal(["g z", "b c", "d e", "f"], Lines(rows));

        _cards[1] = new Card("y");
        Assert.Equal(["g y", "b c", "d e", "f"], Lines(rows));

        _cards.Clear();
        Assert.Empty(rows.Rows);
    }

    [Fact]
    public void LeavesTheRowsACardAddedAtTheEndDoesntChange()
    {
        CardRows rows = CardRows.Of(_cards);
        rows.Columns = 2;
        CardRow first = rows.Rows[0];
        CardRow last = rows.Rows[2];
        List<NotifyCollectionChangedAction> firstChanges = [];
        List<NotifyCollectionChangedAction> rowChanges = [];
        first.Cards.CollectionChanged += (_, change) => firstChanges.Add(change.Action);
        rows.Rows.CollectionChanged += (_, change) => rowChanges.Add(change.Action);

        _cards.Add(new Card("f"));
        _cards.Add(new Card("g"));

        Assert.Empty(firstChanges);
        Assert.Same(last, rows.Rows[2]);
        Assert.Equal([NotifyCollectionChangedAction.Add], rowChanges);
    }

    [Fact]
    public void DropsTheRowsNoLongerFilled()
    {
        CardRows rows = CardRows.Of(_cards);
        rows.Columns = 2;

        _cards.RemoveAt(4);
        _cards.RemoveAt(3);

        Assert.Equal(["a b", "c"], Lines(rows));
    }

    [Fact]
    public void FollowsTheChangesOfABatchOnceItEnds()
    {
        CardRows rows = CardRows.Of(_cards);
        rows.Columns = 2;
        List<string[]> seen = [];
        _cards.CollectionChanged += (_, _) => seen.Add(Lines(rows));

        rows.Batch(() =>
        {
            _cards.Move(4, 0);
            _cards.Move(4, 1);
        });

        Assert.Equal([["a b", "c d", "e"], ["a b", "c d", "e"]], seen);
        Assert.Equal(["e d", "a b", "c"], Lines(rows));
    }

    [Fact]
    public void ABatchWithinABatchWaitsForTheOuterOne()
    {
        CardRows rows = CardRows.Of(_cards);
        rows.Columns = 2;
        List<string> afterInner = [];

        rows.Batch(() =>
        {
            rows.Batch(() => _cards.Move(4, 0));
            afterInner.AddRange(Lines(rows));
        });

        Assert.Equal(["a b", "c d", "e"], afterInner);
        Assert.Equal(["e a", "b c", "d"], Lines(rows));
    }

    [Fact]
    public void ABatchThatFailsStillFollowsWhatItChanged()
    {
        CardRows rows = CardRows.Of(_cards);
        rows.Columns = 2;

        Assert.Throws<InvalidOperationException>(() => rows.Batch(() =>
        {
            _cards.RemoveAt(0);
            throw new InvalidOperationException("Stopped.");
        }));

        Assert.Equal(["b c", "d e"], Lines(rows));

        _cards.RemoveAt(0);
        Assert.Equal(["c d", "e"], Lines(rows));
    }

    [Fact]
    public void NeedsAListAndAChange()
    {
        Assert.Throws<ArgumentNullException>(() => CardRows.Of<Card>(null!));
        Assert.Throws<ArgumentNullException>(() => CardRows.Of(_cards).Batch(null!));
    }

    private static string[] Lines(CardRows rows)
        => [.. rows.Rows.Select(row => string.Join(' ', row.Cards.Cast<Card>().Select(card => card.Name)))];

    private sealed record Card(string Name);
}
