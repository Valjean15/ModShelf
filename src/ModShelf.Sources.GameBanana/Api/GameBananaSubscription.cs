using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// One of what a member subscribes to, in their subscriptions.
/// </summary>
internal sealed record GameBananaSubscription
{
    [JsonPropertyName("_aSubscription")]
    public GameBananaSubscribed? Subscription { get; init; }
}
