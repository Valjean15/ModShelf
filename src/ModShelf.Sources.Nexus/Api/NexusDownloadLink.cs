using System.Text.Json.Serialization;

namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// One server a file can be downloaded from, from <c>v1/games/{game}/mods/{mod}/files/{file}/download_link.json</c>.
/// </summary>
/// <param name="Url">The signed link to the file. Secret: never store, cache or log it.</param>
internal sealed record NexusDownloadLink(string? Name, string? ShortName, [property: JsonPropertyName("URI")] string? Url)
{
    public override string ToString() => $"{nameof(NexusDownloadLink)} {{ ShortName = {ShortName} }}";
}
