using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A fake source on the <see cref="FakeAccountSite"/>, with one call to its API to make once the account is linked.
/// </summary>
internal interface IFakeAccountSource : IAccountLinker
{
    /// <summary>
    /// Asks the site whose account the request carries, as the source sends it: the app adds the linked account.
    /// </summary>
    /// <exception cref="AuthenticationFailedException">The site refused the request, or it carried no account.</exception>
    Task<AccountProfile> WhoAmIAsync(CancellationToken cancellationToken);
}
