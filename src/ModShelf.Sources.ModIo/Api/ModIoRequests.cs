using ModShelf.Sources.ModIo.Accounts;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// What the source, its strategy and its handlers note on a request for one another.
/// </summary>
internal static class ModIoRequests
{
    /// <summary>
    /// The source notes that mod.io answers the request for an access token only, as it lists subscriptions, and gives
    /// some games' files: it refuses an API key with a 401, which the app would take for the key no longer working.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> NeedsToken = new("ModShelf.ModIo.NeedsToken");

    /// <summary>
    /// The strategy notes which credential it added.
    /// </summary>
    public static readonly HttpRequestOptionsKey<ModIoCredential> Sent = new("ModShelf.ModIo.Sent");

    /// <summary>
    /// <see cref="ModIoTokenGate"/> notes that it answered the request itself, with a 403, without sending it, as it needs
    /// an access token and an API key is linked.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> HeldForToken = new("ModShelf.ModIo.HeldForToken");
}
