using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.ModIo.Api;
using ModShelf.Sources.ModIo.Catalog;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// The <see cref="IDownloadResolver"/> half: where a file downloads from right now. mod.io's link for it, read afresh
/// each time, sends the download on to its file server, which takes no credentials; ModShelf asks where, with the
/// account, and hands that on with the file's MD5 checksum.
/// </summary>
public sealed partial class ModIoSource : IDownloadResolver
{
    /// <summary>
    /// What mod.io's virus scan says of a malicious file, and of one that may be harmful, such as one with a program in it.
    /// </summary>
    private const int Malicious = 1;

    private const int MayBeHarmful = 2;

    /// <summary>
    /// False: mod.io's files are always downloaded from their link.
    /// </summary>
    public bool HandsOverLinks => false;

    /// <summary>
    /// The flag of a game that gives its files to an access token only.
    /// </summary>
    private const int DownloadsNeedToken = 4;

    /// <returns>The file's download, never a handoff: mod.io has no scheme of its own.</returns>
    /// <exception cref="ArgumentException">The file isn't mod.io's.</exception>
    /// <exception cref="AuthenticationFailedException">
    /// No key or token is linked, or mod.io refused it; or the game gives its files to an access token only, and an API
    /// key is linked: the request isn't sent, as mod.io's refusal would have the app ask for the key to be linked again.
    /// </exception>
    /// <exception cref="HttpRequestException">
    /// mod.io couldn't be reached, or refused; with 403 when the game is closed to apps, 404 when the file doesn't exist,
    /// 410 when mod.io's virus scan found it malicious.
    /// </exception>
    public async Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        (long game, long mod) = ModId(file.Mod);
        long fileId = Links.ModIoLink.IsId(file.Id)
            ? Links.ModIoLink.Id(file.Id)
            : throw new ArgumentException($"The file '{file.Id}' isn't mod.io's: its files are numbers.", nameof(file));

        ModIoModfile found = await _api.GetAsync<ModIoModfile>(
                GameApi(game, $"games/{Number(game)}/mods/{Number(mod)}/files/{Number(fileId)}"),
                cancellationToken)
            .ConfigureAwait(false);
        if (found.VirusPositive == Malicious)
        {
            LogMalicious(_logger, fileId, mod);
            throw new HttpRequestException(
                "mod.io's virus scan found the file malicious, so ModShelf doesn't download it.", null, HttpStatusCode.Gone);
        }

        if (found.VirusPositive == MayBeHarmful)
        {
            LogMayBeHarmful(_logger, fileId, mod);
        }

        Uri link = OnApi(found.Download?.BinaryUrl, game)
            ?? throw new HttpRequestException(HttpRequestError.InvalidResponse, "mod.io gave no link on its API to download the file by.");
        bool needsToken = ((_access.TryGetValue(game, out int access)
            ? access
            : (await GameAsync(game, cancellationToken).ConfigureAwait(false)).ApiAccessOptions) & DownloadsNeedToken) != 0;
        Uri server = await _api.GetRedirectAsync(
                link,
                needsToken ? () => new AuthenticationFailedException("mod.io gives this game's files to an access token only: link one in place of your API key.") : null,
                cancellationToken)
            .ConfigureAwait(false);
        if (ModIoMods.WebLink(server.AbsoluteUri) is null)
        {
            throw new HttpRequestException(HttpRequestError.InvalidResponse, "mod.io sent the download somewhere other than a web address.");
        }

        return new DownloadTicket(
            server,
            expectedHash: ModIoFiles.Hash(found),
            expiresAt: found.Download?.DateExpires is > 0 and var expires ? ModIoMods.Time(expires) : null);
    }

    /// <summary>
    /// The link, when it's on the game's API or mod.io's, where the account may be sent; null otherwise.
    /// </summary>
    private Uri? OnApi(string? text, long game)
        => ModIoMods.WebLink(text) is { } link && (IsUnder(link, GameApi(game)) || IsUnder(link, _options.ApiAddress)) ? link : null;

    private static bool IsUnder(Uri link, Uri address)
        => link.Scheme == address.Scheme
            && string.Equals(link.Host, address.Host, StringComparison.OrdinalIgnoreCase)
            && link.Port == address.Port
            && link.AbsolutePath.StartsWith(address.AbsolutePath, StringComparison.Ordinal);
}
