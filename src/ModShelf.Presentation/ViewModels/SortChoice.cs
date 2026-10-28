using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// An order for a search's results, as the sort box lists it: a sort and its direction, such as "Least downloaded".
/// </summary>
public sealed record SortChoice(SearchSort Sort, SearchDirection Direction, string Name)
{
    public override string ToString() => Name;
}
