using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// The answer mod.io gives when it refuses.
/// </summary>
internal sealed record ModIoErrorAnswer
{
    [JsonPropertyName("error")]
    public ModIoError? Error { get; init; }
}
