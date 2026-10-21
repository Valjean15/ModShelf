namespace ModShelf.Domain.Mods;

/// <summary>
/// A mod on one source. <see cref="Id"/> is the source's own ID for the mod within <see cref="Game"/>.
/// </summary>
public sealed record ModRef
{
    public ModRef(GameRef game, string id)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Game = game;
        Id = id;
    }

    public GameRef Game { get; }

    public string Id { get; }
}
