using System.Net.Http.Headers;

using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.ModIo.Api;

namespace ModShelf.Sources.ModIo.Accounts;

/// <summary>
/// Links an account with what the user makes for themselves on mod.io's API Access page, whichever they choose: their
/// API key, sent as <c>api_key</c> in each request's query, or a personal access token, sent as a bearer token. One box
/// takes both, told apart by their shape (ADR 0089).
/// </summary>
/// <remarks>
/// mod.io checks them when the source looks up whose they are. Neither can be refreshed: one mod.io refuses has to be
/// linked again. Both are secret, so neither is ever logged, and messages never quote them.
/// </remarks>
internal sealed class ModIoAccessStrategy : IAuthStrategy
{
    /// <summary>
    /// The query parameter mod.io reads an API key from.
    /// </summary>
    public const string KeyParameter = "api_key";

    /// <summary>
    /// How long an API key is: 32 hexadecimal characters.
    /// </summary>
    private const int KeyLength = 32;

    private const string Bearer = "Bearer";

    /// <summary>
    /// Where a signed-in user makes their API key and their access tokens.
    /// </summary>
    public static readonly Uri AccessPage = new("https://mod.io/me/access");

    public AuthKind Kind => AuthKind.ApiKey;

    /// <summary>
    /// Which credential <paramref name="credentials"/> is, by its shape: 32 hexadecimal characters are an API key, and
    /// anything else a token.
    /// </summary>
    public static ModIoCredential Of(AccountCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return IsKey(credentials.AccessToken) ? ModIoCredential.ApiKey : ModIoCredential.AccessToken;
    }

    public Task<AuthChallenge> BeginAsync(AuthStart start, CancellationToken cancellationToken)
        => Task.FromResult(new AuthChallenge(AuthKind.ApiKey, AccessPage));

    /// <summary>
    /// The API key or access token pasted, without the spaces around it, or a <c>Bearer</c> before a token.
    /// </summary>
    /// <exception cref="AuthenticationFailedException">It's neither an API key nor a token, by its shape.</exception>
    public Task<AccountCredentials> CompleteAsync(AuthChallenge challenge, string answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);

        string pasted = answer.Trim();
        if (pasted.StartsWith(Bearer + " ", StringComparison.OrdinalIgnoreCase))
        {
            pasted = pasted[(Bearer.Length + 1)..].Trim();
        }

        return IsKey(pasted) || IsToken(pasted)
            ? Task.FromResult(new AccountCredentials(pasted))
            : throw new AuthenticationFailedException(
                "That's neither a mod.io API key, 32 letters and digits, nor an access token, which is far longer. Copy one from the API Access page of your mod.io account.");
    }

    /// <summary>
    /// Adds an API key as the request's <c>api_key</c>, or a token as its bearer token, in place of either sent before,
    /// and notes which (<see cref="ModIoRequests.Sent"/>).
    /// </summary>
    public void Authorize(HttpRequestMessage request, AccountCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        ModIoCredential credential = Of(credentials);
        request.Headers.Authorization = credential == ModIoCredential.AccessToken
            ? new AuthenticationHeaderValue(Bearer, credentials.AccessToken)
            : null;
        if (request.RequestUri is { } address)
        {
            request.RequestUri = WithKey(address, credential == ModIoCredential.ApiKey ? credentials.AccessToken : null);
        }

        request.Options.Set(ModIoRequests.Sent, credential);
    }

    public Task<AccountCredentials?> RefreshAsync(AccountCredentials expired, CancellationToken cancellationToken)
        => Task.FromResult<AccountCredentials?>(null);

    /// <summary>
    /// Whether the text is shaped as an API key: 32 hexadecimal characters.
    /// </summary>
    internal static bool IsKey(string text) => text.Length == KeyLength && text.All(char.IsAsciiHexDigit);

    /// <summary>
    /// Whether the text is shaped as an access token: longer than a key, of the characters a token is written in, as the
    /// three parts of a JSON Web Token.
    /// </summary>
    internal static bool IsToken(string text)
        => text.Length > KeyLength && text.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '~' or '+' or '/' or '=');

    /// <summary>
    /// The address with <paramref name="key"/> as its <c>api_key</c>, in place of any it had; or without one, when null.
    /// </summary>
    private static Uri WithKey(Uri address, string? key)
    {
        (string path, string query) = address.IsAbsoluteUri
            ? (address.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped),
                address.GetComponents(UriComponents.Query, UriFormat.UriEscaped))
            : Split(address.OriginalString);
        string[] pairs =
        [
            .. query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(pair => !string.Equals(pair.Split('=', 2)[0], KeyParameter, StringComparison.OrdinalIgnoreCase)),
            .. key is null ? Array.Empty<string>() : [$"{KeyParameter}={Uri.EscapeDataString(key)}"],
        ];
        string written = pairs.Length == 0 ? path : $"{path}?{string.Join('&', pairs)}";
        return new Uri(written, address.IsAbsoluteUri ? UriKind.Absolute : UriKind.Relative);
    }

    private static (string Path, string Query) Split(string address)
    {
        int question = address.IndexOf('?', StringComparison.Ordinal);
        return question < 0 ? (address, string.Empty) : (address[..question], address[(question + 1)..]);
    }
}
