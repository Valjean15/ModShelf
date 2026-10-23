namespace ModShelf.Sources.Abstractions.Accounts;

/// <summary>
/// How a user proves to a source who they are.
/// </summary>
/// <remarks>
/// The numeric values are stored with linked accounts, so never renumber them. A source that works without
/// any account doesn't need a kind: it either says so through <see cref="IAccountLinker.WorksWithoutAccount"/>
/// or doesn't implement <see cref="IAccountLinker"/> at all.
/// </remarks>
public enum AuthKind
{
    /// <summary>The user pastes a personal API key from the site.</summary>
    ApiKey = 1,

    /// <summary>The user approves ModShelf in the browser (OAuth 2.0 with PKCE and a loopback redirect).</summary>
    OAuthPkce = 2,

    /// <summary>The site emails the user a code, which the user types in.</summary>
    EmailCode = 3,
}
