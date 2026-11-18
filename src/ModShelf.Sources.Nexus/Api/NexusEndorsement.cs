namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// What the account said of a mod, from <c>v1/user/endorsements.json</c>.
/// </summary>
/// <param name="DomainName">The game's domain name, such as <c>stardewvalley</c>.</param>
/// <param name="Status">Such as <c>Endorsed</c> or <c>Abstained</c>.</param>
internal sealed record NexusEndorsement(long ModId, string? DomainName, string? Status);
