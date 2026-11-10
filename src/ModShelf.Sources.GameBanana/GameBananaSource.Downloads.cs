using System.Globalization;
using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Catalog;
using ModShelf.Sources.GameBanana.Links;

namespace ModShelf.Sources.GameBanana;

/// <summary>
/// The <see cref="IDownloadResolver"/> half: GameBanana's link for a file, and its checksum, read afresh every time one
/// is needed. Downloads need no account, and no handoff.
/// </summary>
public sealed partial class GameBananaSource : IDownloadResolver
{
    /// <param name="file">One of this source's files.</param>
    /// <param name="handoff">Ignored: GameBanana hands no downloads over.</param>
    /// <returns>A ticket for GameBanana's link to the file, with its MD5 checksum when GameBanana gives one.</returns>
    /// <exception cref="ArgumentException"><paramref name="file"/> isn't GameBanana's.</exception>
    /// <exception cref="HttpRequestException">
    /// GameBanana couldn't be reached, or refused; with 404 when the mod or the file is gone.
    /// </exception>
    public async Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        string modId = ModId(file.Mod);
        if (!GameBananaLink.IsId(file.Id))
        {
            throw new ArgumentException($"The file '{file.Id}' isn't GameBanana's: its files are numbers.", nameof(file));
        }

        GameBananaMod profile = await ProfileAsync(modId, cancellationToken).ConfigureAwait(false);
        GameBananaFile listed = GameBananaFiles.Find(profile, file.Id)
            ?? throw new HttpRequestException("GameBanana no longer lists the file.", null, HttpStatusCode.NotFound);

        return new DownloadTicket(DownloadLink(listed), expectedHash: GameBananaFiles.Hash(listed));
    }

    /// <summary>
    /// GameBanana's link for the file, on GameBanana itself; the one it makes for every file when it gives another.
    /// </summary>
    private static Uri DownloadLink(GameBananaFile file)
        => GameBananaMods.WebLink(file.DownloadUrl) is { } given
            && (given.Host.Equals(GameBananaLink.Host, StringComparison.OrdinalIgnoreCase)
                || given.Host.EndsWith($".{GameBananaLink.Host}", StringComparison.OrdinalIgnoreCase))
            ? given
            : new Uri($"https://{GameBananaLink.Host}/dl/{file.Id.ToString(CultureInfo.InvariantCulture)}");
}
