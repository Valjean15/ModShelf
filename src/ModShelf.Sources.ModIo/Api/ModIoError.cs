using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// Why mod.io refused.
/// </summary>
internal sealed record ModIoError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>
    /// Which refusal it is, such as 14000 for something that doesn't exist.
    /// </summary>
    [JsonPropertyName("error_ref")]
    public int ErrorRef { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
