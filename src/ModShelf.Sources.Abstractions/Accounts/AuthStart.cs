namespace ModShelf.Sources.Abstractions.Accounts;

/// <summary>
/// What the app knows when linking starts, before the user does anything.
/// </summary>
public sealed record AuthStart
{
    public AuthStart(Uri? redirectUri = null, string? email = null)
    {
        if (redirectUri is not null && !(redirectUri.IsAbsoluteUri && redirectUri.IsLoopback && WebUrls.IsHttpOrHttps(redirectUri)))
        {
            throw new ArgumentException("The redirect URI must be an http or https address on this computer (loopback).", nameof(redirectUri));
        }

        if (email is not null && (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal)))
        {
            throw new ArgumentException("That isn't an email address.", nameof(email));
        }

        RedirectUri = redirectUri;
        Email = email?.Trim();
    }

    /// <summary>
    /// For <see cref="AuthKind.OAuthPkce"/>: where the browser returns once the user approves, a loopback
    /// address the app listens on.
    /// </summary>
    public Uri? RedirectUri { get; }

    /// <summary>
    /// For <see cref="AuthKind.EmailCode"/>: where the site sends the code.
    /// </summary>
    public string? Email { get; }
}
