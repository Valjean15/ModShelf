namespace ModShelf.Domain.Mods;

/// <summary>
/// A game as one source knows it: the source, plus that source's own key for the game,
/// such as Nexus Mods' <c>skyrimspecialedition</c>.
/// </summary>
public sealed record GameRef
{
    public GameRef(SourceId source, string key)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Source = source;
        Key = key;
    }

    public SourceId Source { get; }

    public string Key { get; }
}
