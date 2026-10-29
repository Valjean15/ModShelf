using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Application.Accounts;

/// <summary>
/// A source that can link an account, as <see cref="IAccountService.ListAsync"/> reports it.
/// </summary>
/// <param name="Source">The source's ID.</param>
/// <param name="DisplayName">The source's name, such as "Nexus Mods".</param>
/// <param name="WorksWithoutAccount">Whether the source can be used at all without an account.</param>
/// <param name="AuthKinds">The ways the source offers to link an account, in the order to offer them.</param>
/// <param name="Account">The linked account, or null when there's none.</param>
/// <param name="Quota">
/// What's left of the linked account's quota, as the site last said while the app runs; null without an account, for a
/// site without a quota, or before the site has answered.
/// </param>
public sealed record SourceAccountStatus(
    SourceId Source,
    string DisplayName,
    bool WorksWithoutAccount,
    IReadOnlyList<AuthKind> AuthKinds,
    LinkedAccount? Account,
    ApiQuota? Quota = null);
