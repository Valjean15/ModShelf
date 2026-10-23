namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// What's left of the API's allowances, from a response's <c>X-RL-*</c> headers.
/// </summary>
internal sealed record NexusRateLimit(
    int HourlyLimit,
    int HourlyRemaining,
    DateTimeOffset HourlyReset,
    int DailyLimit,
    int DailyRemaining,
    DateTimeOffset DailyReset);
