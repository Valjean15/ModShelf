using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Accounts;

/// <summary>
/// A source's stored credentials and how they were obtained, which decides how they're refreshed.
/// </summary>
/// <param name="Kind">How the account was linked.</param>
/// <param name="Credentials">The credentials. Secret.</param>
internal sealed record VaultEntry(AuthKind Kind, AccountCredentials Credentials)
{
    /// <summary>
    /// Whether the credentials were found to be beyond refreshing, so the account has to be linked again.
    /// Kept in memory only: after a restart, one more refresh is tried.
    /// </summary>
    public bool NeedsRelink { get; init; }

    public override string ToString() => $"VaultEntry {{ Kind = {Kind}, Credentials = {Credentials}, NeedsRelink = {NeedsRelink} }}";
}
