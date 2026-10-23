namespace ModShelf.Sources.Abstractions.Accounts;

/// <summary>
/// The site refused an API key, code, authorization or token. Distinct from network errors, so the app can
/// tell the user to check what they entered, or to link again, rather than to retry.
/// </summary>
/// <remarks>The message must never contain the secret that was refused.</remarks>
public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException()
        : this("The site refused the credentials.")
    {
    }

    public AuthenticationFailedException(string message)
        : base(message)
    {
    }

    public AuthenticationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
