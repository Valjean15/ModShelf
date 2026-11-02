using System.Text.Json.Nodes;

namespace ModShelf.Sources.Nexus.Catalog;

/// <summary>
/// The variables of the <c>ModDetails</c> and <c>ModRequirements</c> queries: mods by their game's domain name and ID.
/// The v2 API's <c>mods</c> wants the game's number with a mod ID; <c>legacyModsByDomain</c> takes its domain name.
/// </summary>
internal static class NexusModIds
{
    public static JsonObject For(string game, long modId)
        => new()
        {
            ["ids"] = new JsonArray(new JsonObject { ["gameDomain"] = game, ["modId"] = modId }),
        };
}
