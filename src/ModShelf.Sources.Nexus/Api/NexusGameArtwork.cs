namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// What the <c>GameArtwork</c> query answers: <c>data.gameArtwork</c>, the addresses of each kind of game picture, with
/// <c>{gameId}</c> for the game's number.
/// </summary>
internal sealed record NexusGameArtwork(NexusGameArtwork.Schemas? GameArtwork)
{
    internal sealed record Schemas(SchemaV2? SchemaV2);

    /// <param name="Hero">The wide banner at the top of the game's page, such as 1,920 by 620.</param>
    /// <param name="Thumbnail">The small square picture, such as 80 by 80.</param>
    internal sealed record SchemaV2(string? Hero, string? Thumbnail);
}
