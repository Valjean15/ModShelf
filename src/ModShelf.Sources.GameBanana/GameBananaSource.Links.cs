using System.Globalization;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Catalog;
using ModShelf.Sources.GameBanana.Links;

namespace ModShelf.Sources.GameBanana;

/// <summary>
/// The <see cref="ILinkRecognizer"/> half: which file a link the user gives points to, and what the file is called.
/// GameBanana's links don't name a mod's game, so each is asked of the mod's profile, which names it.
/// </summary>
public sealed partial class GameBananaSource : ILinkRecognizer
{
    /// <summary>
    /// Recognizes links to a mod's page or download page, and 1-click links that name their mod, as they are or behind a
    /// mod manager's scheme. A link to the mod as a whole means the archive it got last.
    /// </summary>
    /// <returns>The file, or null when the link isn't GameBanana's.</returns>
    /// <exception cref="ArgumentException">
    /// The link is GameBanana's, but to something other than a mod or a file of one, to a file without naming its mod,
    /// or to a mod without that file, or without files.
    /// </exception>
    /// <exception cref="HttpRequestException">
    /// GameBanana couldn't be reached, or refused; with 404 when the mod doesn't exist.
    /// </exception>
    public async Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        if (GameBananaLink.Read(link) is not { } banana)
        {
            return null;
        }

        GameBananaMod profile = await ProfileAsync(banana.ModId, cancellationToken).ConfigureAwait(false);
        if (profile.Game is not { } game)
        {
            throw new HttpRequestException(HttpRequestError.InvalidResponse, "GameBanana didn't say which game the mod is of.");
        }

        GameBananaFile file = banana.FileId is { } fileId
            ? GameBananaFiles.Find(profile, fileId)
                ?? throw new ArgumentException("The GameBanana mod has no such file. Copy the mod's page link instead.", nameof(link))
            : GameBananaFiles.MainDownload(profile)
                ?? throw new ArgumentException("The GameBanana mod has no files to download.", nameof(link));

        ModFileRef found = new(new ModRef(GameOf(game.Id), banana.ModId), file.Id.ToString(CultureInfo.InvariantCulture));
        return new RecognizedLink(found, GameBananaFiles.FileName(file));
    }
}
