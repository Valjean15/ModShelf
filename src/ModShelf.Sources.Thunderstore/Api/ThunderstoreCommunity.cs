namespace ModShelf.Sources.Thunderstore.Api;

/// <summary>
/// A community, a game, as <c>api/cyberstorm/community/</c> lists it.
/// </summary>
/// <param name="Identifier">Its permanent identifier, such as <c>lethal-company</c>: the game's key.</param>
/// <param name="BackgroundImageUrl">A wide picture of the game, 1920 by 1080, made to frame the community's page.</param>
/// <param name="HeroImageUrl">The banner at the top of the community's page, 1920 by 620, for nearly all.</param>
/// <param name="CommunityIconUrl">A square icon, 192 by 192, for some.</param>
internal sealed record ThunderstoreCommunity(
    string Identifier,
    string Name,
    string? BackgroundImageUrl = null,
    string? HeroImageUrl = null,
    string? CommunityIconUrl = null);
