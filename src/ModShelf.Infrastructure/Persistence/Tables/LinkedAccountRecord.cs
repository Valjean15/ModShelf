using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// A row of the <c>LinkedAccounts</c> table: whose account is linked on a source, and how. The repository maps it
/// to and from <see cref="LinkedAccount"/>.
/// </summary>
/// <remarks>
/// Holds no secrets: the credentials live in the operating system's secret store. A mutable class rather than a
/// record, because EF Core tracks entities by reference.
/// </remarks>
internal sealed class LinkedAccountRecord
{
    /// <summary>
    /// The source's ID, and the row's key: one account per source.
    /// </summary>
    public required string SourceId { get; set; }

    public AuthKind Kind { get; set; }

    public required string DisplayName { get; set; }

    public string? AvatarUrl { get; set; }

    public string? Tier { get; set; }

    public DateTimeOffset LinkedAt { get; set; }

    public static LinkedAccountRecord From(LinkedAccount account)
    {
        LinkedAccountRecord record = new() { SourceId = account.Source.Value, DisplayName = account.Profile.DisplayName };
        record.CopyFrom(account);
        return record;
    }

    /// <summary>
    /// Takes over everything but the key from <paramref name="account"/>, which replaces the source's earlier account.
    /// </summary>
    public void CopyFrom(LinkedAccount account)
    {
        Kind = account.Kind;
        DisplayName = account.Profile.DisplayName;
        AvatarUrl = account.Profile.AvatarUrl?.AbsoluteUri;
        Tier = account.Profile.Tier;
        LinkedAt = account.LinkedAt;
    }

    /// <summary>
    /// Rebuilds the account this row stores.
    /// </summary>
    /// <exception cref="InvalidDataException">The row holds values no account can have.</exception>
    public LinkedAccount ToAccount()
    {
        try
        {
            Uri? avatar = AvatarUrl is null ? null : new Uri(AvatarUrl, UriKind.Absolute);
            return new LinkedAccount(new SourceId(SourceId), Kind, new AccountProfile(DisplayName, avatar, Tier), LinkedAt);
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            throw new InvalidDataException(
                $"The account linked on {SourceId} in the library database can't be loaded: {exception.Message}", exception);
        }
    }
}
