namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// Whose account a key or token belongs to, from <c>v1/users/validate.json</c>.
/// </summary>
/// <remarks>
/// The answer also holds the key itself and the account's email; neither is read, so neither is ever kept.
/// </remarks>
internal sealed record NexusUser(long UserId, string Name, bool IsPremium, bool IsSupporter, string? ProfileUrl);
