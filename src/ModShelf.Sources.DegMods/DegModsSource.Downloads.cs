using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Http;
using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Downloads;
using ModShelf.Sources.DegMods.Links;

namespace ModShelf.Sources.DegMods;

/// <summary>
/// The <see cref="IDownloadResolver"/> half: where to download a file from, read afresh from the mod's newest event
/// every time. A file named by its SHA-256 is asked of the file servers, the one its link names first, unless that's DEG
/// Mods' own; DEG Mods' own gives files only to a browser that showed its ad, so a file only it has opens the mod's page
/// (ADR 0090). Another host's link is asked what it gives: the file is downloaded from it, and a page, such as
/// MediaFire's, opens.
/// </summary>
public sealed partial class DegModsSource : IDownloadResolver
{
    /// <summary>
    /// The most redirects followed asking a link what it gives.
    /// </summary>
    private const int MaxRedirects = 5;

    /// <summary>
    /// False: a file on a page, or only on DEG Mods' own server, is downloaded by the browser.
    /// </summary>
    public bool HandsOverLinks => false;

    /// <param name="file">One of this source's files.</param>
    /// <param name="handoff">Ignored: DEG Mods hands no downloads over.</param>
    /// <returns>
    /// A ticket for a file server that has the file, with its SHA-256, or the file's own link when it gives the file; or
    /// <see cref="HandoffRequired"/> with the page to download it from in a browser.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="file"/> isn't DEG Mods'.</exception>
    /// <exception cref="HttpRequestException">
    /// No relay could be read; with 404 when the mod or the file is gone, or DEG Mods hides the mod.
    /// </exception>
    public async Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ModKey(file.Mod);
        if (!DegModsLink.IsFileId(file.Id))
        {
            throw new ArgumentException($"The file '{file.Id}' isn't DEG Mods': its files are 16 hexadecimal digits.", nameof(file));
        }

        DegMod mod = await ShownAsync(file.Mod, cancellationToken).ConfigureAwait(false);
        DegDownload download = mod.Downloads.FirstOrDefault(listed => listed.Id == file.Id)
            ?? throw new HttpRequestException("DEG Mods no longer lists the file.", null, HttpStatusCode.NotFound);

        string? sha256 = download.Sha256 ?? FileHosts.BlossomHash(download.Url);
        FileHash? hash = sha256 is null ? null : new FileHash(FileHashAlgorithm.Sha256, sha256);
        if (FileHosts.BlossomHash(download.Url) is null)
        {
            if (FileHosts.IsDirect(download.Url) || await GivesFileAsync(download.Url, cancellationToken).ConfigureAwait(false))
            {
                return new DownloadTicket(download.Url, expectedHash: hash);
            }

            LogOnAPage(_logger, download.Url.Host);
            return new HandoffRequired(download.Url);
        }

        bool gated = IsGated(download.Url);
        foreach (Uri server in Servers(download.Url, gated, sha256!))
        {
            if (await HasAsync(server, cancellationToken).ConfigureAwait(false))
            {
                LogFoundOn(_logger, file.Id, server.Host);
                return new DownloadTicket(server, expectedHash: hash);
            }
        }

        LogOnlyGated(_logger, file.Id);
        return new HandoffRequired(ModPage(mod));
    }

    private bool IsGated(Uri link) => _options.GatedHosts.Contains(link.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where to ask for the file, in order: its link, unless that's DEG Mods' own server; then each file server, by its
    /// SHA-256 alone, as Blossom names files, each host once.
    /// </summary>
    private IEnumerable<Uri> Servers(Uri link, bool gated, string sha256)
    {
        HashSet<string> asked = new(StringComparer.OrdinalIgnoreCase);
        if (!gated && asked.Add(link.Host))
        {
            yield return link;
        }

        foreach (Uri server in _options.FileServers)
        {
            if (!_options.GatedHosts.Contains(server.Host, StringComparer.OrdinalIgnoreCase) && asked.Add(server.Host))
            {
                yield return new Uri(server, sha256);
            }
        }
    }

    /// <summary>
    /// Whether a link on another host gives the file itself rather than a page: it answers HEAD with something other
    /// than a web page, there or where it sends ModShelf, at most five times. A link to this computer or its network is
    /// never asked, nor one that doesn't answer: a browser opens it.
    /// </summary>
    private async Task<bool> GivesFileAsync(Uri link, CancellationToken cancellationToken)
    {
        HttpClient client = _clients.CreateClient(SourceHttpClients.NameFor(DegModsId));
        Uri address = link;
        for (int sent = 0; sent <= MaxRedirects; sent++)
        {
            if (FileHosts.IsOnThisNetwork(address))
            {
                LogOnThisNetwork(_logger, address.Host);
                return false;
            }

            using HttpRequestMessage request = new(HttpMethod.Head, address);
            try
            {
                using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is not { } location)
                {
                    return FileHosts.IsFile(response);
                }

                address = new Uri(address, location);
                if (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp)
                {
                    return false;
                }
            }
            catch (HttpRequestException exception)
            {
                LogLinkFailed(_logger, exception, address.Host);
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a file server has the file: it answers HEAD with the file, or with where it is.
    /// </summary>
    private async Task<bool> HasAsync(Uri address, CancellationToken cancellationToken)
    {
        HttpClient client = _clients.CreateClient(SourceHttpClients.NameFor(DegModsId));
        using HttpRequestMessage request = new(HttpMethod.Head, address);
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return (int)response.StatusCode is >= 200 and < 400;
        }
        catch (HttpRequestException exception)
        {
            LogServerFailed(_logger, exception, address.Host);
            return false;
        }
    }
}
