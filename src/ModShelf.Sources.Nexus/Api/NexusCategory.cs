using System.Text.Json;

namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// One of a game's mod categories, from <c>v1/games/{game}.json</c>.
/// </summary>
/// <param name="ParentCategory">The ID of the category it sits in, or <c>false</c> for a top one, such as the one named
/// after the game that the others sit in.</param>
internal sealed record NexusCategory(long CategoryId, string? Name, JsonElement ParentCategory = default)
{
    /// <summary>
    /// Whether it sits in no other category.
    /// </summary>
    public bool IsTop => ParentCategory.ValueKind is JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined;
}
