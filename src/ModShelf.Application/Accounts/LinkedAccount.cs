using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Accounts;

/// <summary>
/// An account linked on a source: who it is and how it was linked. Not secret; the credentials are kept
/// apart, in <see cref="ISecretStore"/>.
/// </summary>
public sealed record LinkedAccount
{
    public LinkedAccount(SourceId source, AuthKind kind, AccountProfile profile, DateTimeOffset linkedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind of authentication.");
        }

        Source = source;
        Kind = kind;
        Profile = profile;
        LinkedAt = linkedAt;
    }

    public SourceId Source { get; }

    /// <summary>
    /// How the account was linked, which decides how its credentials are used and refreshed.
    /// </summary>
    public AuthKind Kind { get; }

    public AccountProfile Profile { get; }

    public DateTimeOffset LinkedAt { get; }
}
