namespace ModShelf.Sources.ModIo.Accounts;

/// <summary>
/// Which of mod.io's two credentials a user made for themselves on its API Access page.
/// </summary>
internal enum ModIoCredential
{
    /// <summary>
    /// Their API key: 32 hexadecimal characters, sent as <c>api_key</c>, read only, 60 requests a minute, and no
    /// subscriptions.
    /// </summary>
    ApiKey = 1,

    /// <summary>
    /// A personal access token: far longer, sent as a bearer token, for a year, 120 requests a minute.
    /// </summary>
    AccessToken = 2,
}
