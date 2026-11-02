namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// One of the ways the Library screen offers to order its games or mods, as its lists show it.
/// </summary>
public sealed record LibraryChoice<T>
{
    internal LibraryChoice(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }

    /// <summary>
    /// Such as "Most mods".
    /// </summary>
    public string Label { get; }

    public override string ToString() => Label;
}
