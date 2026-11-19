using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.CurseForge.Api;
using ModShelf.Sources.CurseForge.Catalog;

namespace ModShelf.Sources.CurseForge;

/// <summary>
/// The <see cref="IDownloadResolver"/> half: a file's download link, as CurseForge gives it, checked by its SHA-1; or,
/// for a mod whose author lets no app download its files, the page that downloads it in the browser, which ModShelf
/// takes the file from as it does other sites' pages.
/// </summary>
public sealed partial class CurseForgeSource : IDownloadResolver
{
    /// <summary>
    /// False: CurseForge's pages hand nothing back; the browser downloads the file.
    /// </summary>
    public bool HandsOverLinks => false;

    /// <exception cref="ArgumentException">The file isn't CurseForge's.</exception>
    /// <exception cref="HttpRequestException">CurseForge has no such file, or couldn't be asked.</exception>
    public async Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
    {
        long id = FileId(file);
        long mod = ModId(file.Mod);
        CurseForgeFile found = (await _api.GetAsync<CurseForgeFile>($"v1/mods/{Number(mod)}/files/{Number(id)}", cancellationToken).ConfigureAwait(false)).Data
            ?? throw new HttpRequestException("CurseForge answered with no file.", null, System.Net.HttpStatusCode.NotFound);
        if (found.ModId != mod)
        {
            throw new HttpRequestException($"CurseForge's file {Number(id)} isn't one of the mod's.", null, System.Net.HttpStatusCode.NotFound);
        }

        IReadOnlyList<FileHash> hashes = CurseForgeFiles.Hashes(found);
        FileHash? hash = hashes.FirstOrDefault(known => known.Algorithm == FileHashAlgorithm.Sha1) ?? (hashes.Count > 0 ? hashes[0] : null);
        if (WebLink(found.DownloadUrl) is { } link)
        {
            return new DownloadTicket(link, expectedHash: hash);
        }

        (CurseForgeMod listed, CurseForgeGame game) = await ModAsync(file.Mod, cancellationToken).ConfigureAwait(false);
        return new HandoffRequired(CurseForgeFiles.DownloadPageOf(PageOf(listed, game), id));
    }
}
