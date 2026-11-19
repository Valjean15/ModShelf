using System.Text.Json.Nodes;

namespace ModShelf.Sources.Thunderstore.Tests.Common;

/// <summary>
/// A package as a test writes it, turned into the JSON a listing's chunk gives, in the format of <c>api/v1/package/</c>.
/// Its versions are given newest first, as Thunderstore lists them; the dependencies are the newest version's.
/// </summary>
internal sealed record TestPackage(string Team, string Name)
{
    public static readonly DateTimeOffset January = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public string FullName => $"{Team}-{Name}";

    public IReadOnlyList<string> Categories { get; init; } = ["Mods"];

    public DateTimeOffset Created { get; init; } = January;

    public DateTimeOffset Updated { get; init; } = January;

    public long Likes { get; init; }

    public bool Deprecated { get; init; }

    public bool Adult { get; init; }

    public string Description { get; init; } = "A package.";

    public IReadOnlyList<string> Dependencies { get; init; } = [];

    public IReadOnlyList<TestVersion> Versions { get; init; } = [new("1.0.0", January)];

    public JsonObject ToJson()
        => new()
        {
            ["name"] = Name,
            ["full_name"] = FullName,
            ["owner"] = Team,
            ["package_url"] = $"https://thunderstore.io/c/game/p/{Team}/{Name}/",
            ["date_created"] = Created.ToString("O"),
            ["date_updated"] = Updated.ToString("O"),
            ["uuid4"] = Guid.NewGuid().ToString(),
            ["rating_score"] = Likes,
            ["is_pinned"] = false,
            ["is_deprecated"] = Deprecated,
            ["has_nsfw_content"] = Adult,
            ["categories"] = new JsonArray([.. Categories.Select(category => (JsonNode?)category)]),
            ["versions"] = new JsonArray(
            [
                .. Versions.Select((version, index) => (JsonNode?)new JsonObject
                {
                    ["name"] = Name,
                    ["full_name"] = $"{FullName}-{version.Number}",
                    ["description"] = Description,
                    ["icon"] = $"https://ccdn.thunderstore.io/live/repository/icons/{FullName}-{version.Number}.png",
                    ["version_number"] = version.Number,
                    ["dependencies"] = new JsonArray([.. (index == 0 ? Dependencies : []).Select(dependency => (JsonNode?)dependency)]),
                    ["download_url"] = $"https://thunderstore.io/package/download/{Team}/{Name}/{version.Number}/",
                    ["downloads"] = version.Downloads,
                    ["date_created"] = version.UploadedAt.ToString("O"),
                    ["website_url"] = string.Empty,
                    ["is_active"] = true,
                    ["uuid4"] = Guid.NewGuid().ToString(),
                    ["file_size"] = version.Size,
                }),
            ]),
        };

    /// <summary>
    /// The packages as a chunk: a JSON array.
    /// </summary>
    public static JsonArray Chunk(params IEnumerable<TestPackage> packages) => new([.. packages.Select(package => (JsonNode?)package.ToJson())]);
}
