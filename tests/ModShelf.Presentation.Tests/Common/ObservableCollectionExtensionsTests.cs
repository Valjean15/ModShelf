using System.Collections.ObjectModel;
using System.Collections.Specialized;

using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.Tests.Common;

public sealed class ObservableCollectionExtensionsTests
{
    private readonly Item _a = new("a");
    private readonly Item _b = new("b");
    private readonly Item _c = new("c");
    private readonly Item _d = new("d");

    [Fact]
    public void MovesInsertsAndRemovesAsFewItemsAsItTakes()
    {
        ObservableCollection<Item> items = [_a, _b, _c];
        List<NotifyCollectionChangedAction> changes = [];
        items.CollectionChanged += (_, e) => changes.Add(e.Action);

        items.SyncWith([_c, _a, _d]);

        Assert.Equal([_c, _a, _d], items);
        Assert.Equal([NotifyCollectionChangedAction.Move, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Remove], changes);
    }

    [Fact]
    public void ItemsAlreadyInPlaceAreLeftAlone()
    {
        ObservableCollection<Item> items = [_a, _b];
        int changes = 0;
        items.CollectionChanged += (_, _) => changes++;

        items.SyncWith([_a, _b]);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void ItemsAreTheSameOnlyWhenTheyAreTheSameInstance()
    {
        ObservableCollection<Item> items = [_a];
        Item lookalike = new("a");

        items.SyncWith([lookalike]);

        Assert.Same(lookalike, Assert.Single(items));
    }

    [Fact]
    public void EmptiesTheList()
    {
        ObservableCollection<Item> items = [_a, _b];

        items.SyncWith([]);

        Assert.Empty(items);
    }

    private sealed record Item(string Name);
}
