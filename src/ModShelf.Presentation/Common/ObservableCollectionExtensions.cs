using System.Collections.ObjectModel;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Changing a list the views show with as few changes as it takes, so its rows keep their state and the list its place.
/// </summary>
internal static class ObservableCollectionExtensions
{
    /// <summary>
    /// Makes <paramref name="items"/> hold <paramref name="wanted"/>, in its order, by moving, inserting and removing
    /// items. Items are the same when they're the same instance.
    /// </summary>
    /// <param name="wanted">The items as they should be, each once.</param>
    public static void SyncWith<T>(this ObservableCollection<T> items, IReadOnlyList<T> wanted)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(wanted);

        for (int index = 0; index < wanted.Count; index++)
        {
            T item = wanted[index];
            if (index < items.Count && ReferenceEquals(items[index], item))
            {
                continue;
            }

            int at = IndexOf(items, item, index + 1);
            if (at >= 0)
            {
                items.Move(at, index);
            }
            else
            {
                items.Insert(index, item);
            }
        }

        while (items.Count > wanted.Count)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    private static int IndexOf<T>(ObservableCollection<T> items, T item, int from)
        where T : class
    {
        for (int index = from; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], item))
            {
                return index;
            }
        }

        return -1;
    }
}
