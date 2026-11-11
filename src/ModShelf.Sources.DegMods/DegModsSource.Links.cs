using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Links;

namespace ModShelf.Sources.DegMods;

/// <summary>
/// The <see cref="ILinkRecognizer"/> half: which file a link to a mod's page, or a mod's <c>nostr:</c> address, points
/// to. The links don't name the mod's game, so each mod is read from the relays, which say it.
/// </summary>
public sealed partial class DegModsSource : ILinkRecognizer
{
    /// <summary>
    /// Recognizes a mod's page, <c>degmods.com/mod/&lt;naddr&gt;</c>, and its address, <c>nostr:naddr1…</c>, as the mod's
    /// main file; and ModShelf's link to one of its files, the page with <c>#file=&lt;id&gt;</c>, as that file.
    /// </summary>
    /// <returns>The file, or null when the link isn't DEG Mods'.</returns>
    /// <exception cref="ArgumentException">
    /// The link is DEG Mods', but not to a mod; to a mod DEG Mods hides, or one without files; or to a file the mod
    /// doesn't list.
    /// </exception>
    /// <exception cref="HttpRequestException">No relay could be read; with 404 when the mod doesn't exist.</exception>
    public async Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (DegModsLink.Read(link, _options.SiteAddress) is not { } degMods)
        {
            return null;
        }

        (ModStatus status, DegMod? mod) = await _index.FindAsync(degMods.PublicKey, degMods.D, cancellationToken).ConfigureAwait(false);
        if (status == ModStatus.Hidden)
        {
            LogHidden(_logger);
            throw new ArgumentException("DEG Mods' moderation hides this mod, so ModShelf doesn't download it.", nameof(link));
        }

        if (mod is null)
        {
            throw new HttpRequestException("DEG Mods has no such mod, or its author deleted it.", null, HttpStatusCode.NotFound);
        }

        DegDownload download = degMods.FileId is { } fileId
            ? mod.Downloads.FirstOrDefault(listed => listed.Id == fileId)
                ?? throw new ArgumentException("The DEG Mods mod no longer lists that file. Copy the link of the mod's page.", nameof(link))
            : mod.Downloads.Count > 0 ? mod.Downloads[0]
            : throw new ArgumentException("The DEG Mods mod has no files to download.", nameof(link));

        return new RecognizedLink(new ModFileRef(ModOf(mod), download.Id), DegModsFiles.FileName(mod, download));
    }
}
