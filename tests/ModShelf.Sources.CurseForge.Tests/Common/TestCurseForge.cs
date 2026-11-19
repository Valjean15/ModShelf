using System.Text.Json.Nodes;

namespace ModShelf.Sources.CurseForge.Tests.Common;

/// <summary>
/// CurseForge's answers as a test writes them: a game, a class, a mod and a file, with what the source reads of each.
/// </summary>
internal static class TestCurseForge
{
    public const long Minecraft = 432;
    public const long Mods = 6;
    public const string ModPage = "https://www.curseforge.com/minecraft/mc-mods/modmenu";

    public static JsonObject Game(long id, string slug, string name)
        => new() { ["id"] = id, ["name"] = name, ["slug"] = slug, ["assets"] = new JsonObject { ["iconUrl"] = $"https://media.forgecdn.net/{slug}-icon.png", ["tileUrl"] = null, ["coverUrl"] = $"https://media.forgecdn.net/{slug}-cover.jpg" } };

    public static JsonObject Class(long id, string name, string slug)
        => new() { ["id"] = id, ["gameId"] = Minecraft, ["name"] = name, ["slug"] = slug, ["isClass"] = true, ["classId"] = null };

    public static JsonObject Mod(long id, string name, string slug, long mainFile, long game = Minecraft, bool distributable = true)
        => new()
        {
            ["id"] = id,
            ["gameId"] = game,
            ["name"] = name,
            ["slug"] = slug,
            ["links"] = new JsonObject { ["websiteUrl"] = $"https://www.curseforge.com/minecraft/mc-mods/{slug}" },
            ["summary"] = $"{name}, for tests.",
            ["status"] = 4,
            ["downloadCount"] = 1234,
            ["primaryCategoryId"] = 423,
            ["classId"] = Mods,
            ["categories"] = new JsonArray(new JsonObject { ["id"] = 423, ["gameId"] = game, ["name"] = "Map and Information", ["slug"] = "map-information" }),
            ["authors"] = new JsonArray(new JsonObject { ["id"] = 1, ["name"] = "Prospector" }),
            ["logo"] = new JsonObject { ["thumbnailUrl"] = $"https://media.forgecdn.net/{slug}-thumb.png", ["url"] = $"https://media.forgecdn.net/{slug}.png" },
            ["mainFileId"] = mainFile,
            ["dateCreated"] = "2019-01-10T00:00:00Z",
            ["dateModified"] = "2026-11-01T12:00:00Z",
            ["dateReleased"] = "2026-10-30T12:00:00Z",
            ["allowModDistribution"] = distributable,
            ["isAvailable"] = true,
        };

    /// <param name="versions">The game versions and loaders it's for.</param>
    /// <param name="releaseType">1 a release, 2 a beta, 3 an alpha.</param>
    public static JsonObject File(
        long id,
        long mod,
        string fileName,
        DateTimeOffset date,
        string[]? versions = null,
        int releaseType = 1,
        int status = 4,
        bool downloadable = true,
        params (long Mod, int Relation)[] dependencies)
        => new()
        {
            ["id"] = id,
            ["gameId"] = Minecraft,
            ["modId"] = mod,
            ["isAvailable"] = true,
            ["displayName"] = $"Display {fileName}",
            ["fileName"] = fileName,
            ["releaseType"] = releaseType,
            ["fileStatus"] = status,
            ["hashes"] = new JsonArray(
                new JsonObject { ["value"] = new string('a', 40), ["algo"] = 1 },
                new JsonObject { ["value"] = new string('b', 32), ["algo"] = 2 }),
            ["fileDate"] = date.ToString("O"),
            ["fileLength"] = 1000 + id,
            ["downloadUrl"] = downloadable ? $"https://edge.forgecdn.net/files/{id / 1000}/{id % 1000}/{fileName}" : null,
            ["gameVersions"] = new JsonArray([.. (versions ?? ["1.20.1", "Fabric"]).Select(version => (JsonNode?)version)]),
            ["dependencies"] = new JsonArray([.. dependencies.Select(dependency => (JsonNode?)new JsonObject { ["modId"] = dependency.Mod, ["relationType"] = dependency.Relation })]),
        };

    /// <summary>
    /// A page of a list, as CurseForge wraps one.
    /// </summary>
    public static JsonObject Page(int index, long total, params JsonNode[] items)
        => new()
        {
            ["data"] = new JsonArray([.. items.Select(item => (JsonNode?)item.DeepClone())]),
            ["pagination"] = new JsonObject { ["index"] = index, ["pageSize"] = 50, ["resultCount"] = items.Length, ["totalCount"] = total },
        };

    /// <summary>
    /// One thing, as CurseForge wraps it.
    /// </summary>
    public static JsonObject Data(JsonNode item) => new() { ["data"] = item.DeepClone() };
}
