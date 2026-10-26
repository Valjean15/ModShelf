using System.Buffers;
using System.Globalization;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="ILinkRecognizer"/> half: which file a link the user gives points to, and what the file is called.
/// </summary>
public sealed partial class NexusSource : ILinkRecognizer
{
    // The characters any of the supported systems forbid in file names, so a downloaded file can move between them.
    private static readonly SearchValues<char> ForbiddenInFileNames = SearchValues.Create(
        [.. Path.GetInvalidFileNameChars(), .. "<>:\"/\\|?*", .. Enumerable.Range(0, 32).Select(code => (char)code)]);

    /// <summary>
    /// Recognizes links to a mod's page and to its files, on the website or as <c>nxm://</c> links, and asks the API
    /// for the file's name. A link to the mod as a whole means its newest main file.
    /// </summary>
    /// <returns>The file, or null when the link isn't Nexus Mods'.</returns>
    /// <exception cref="ArgumentException">
    /// The link is Nexus Mods', but to something other than a mod or a file, or to a mod without main files.
    /// </exception>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the API refused it.</exception>
    /// <exception cref="HttpRequestException">
    /// The API couldn't be reached, or refused for another reason; with 404 when the mod or file doesn't exist.
    /// </exception>
    public async Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        if (NexusLink.Read(link) is not { } nexus)
        {
            return null;
        }

        string mod = $"v1/games/{nexus.Game}/mods/{nexus.ModId}";
        NexusFile file = nexus.FileId is { } fileId
            ? await _api.GetAsync<NexusFile>($"{mod}/files/{fileId}.json", cancellationToken).ConfigureAwait(false)
            : NewestMainFile(await _api.GetAsync<NexusModFiles>($"{mod}/files.json", cancellationToken).ConfigureAwait(false));

        ModFileRef found = new(
            new ModRef(new GameRef(NexusId, nexus.Game), nexus.ModId),
            file.FileId.ToString(CultureInfo.InvariantCulture));
        return new RecognizedLink(found, SuggestFileName(file.FileName), isHandoff: nexus.IsHandoff);
    }

    /// <summary>
    /// The main file uploaded last: what the mod's page offers as its download.
    /// </summary>
    /// <exception cref="ArgumentException">The mod has no main files.</exception>
    private static NexusFile NewestMainFile(NexusModFiles files)
        => files.Files
            .Where(file => file.CategoryId == NexusFile.MainCategory)
            .OrderByDescending(file => file.UploadedTimestamp)
            .ThenByDescending(file => file.FileId)
            .FirstOrDefault()
        ?? throw new ArgumentException(
            "The mod has no main file to download. Open its Files tab and copy the link of the file you want.",
            nameof(files));

    /// <summary>
    /// The name Nexus Mods gives the file, with characters some system forbids replaced by '_'. Null when it gives
    /// none that's usable.
    /// </summary>
    private static string? SuggestFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        Span<char> name = fileName.ToCharArray();
        int forbidden;
        while ((forbidden = name.IndexOfAny(ForbiddenInFileNames)) >= 0)
        {
            name[forbidden] = '_';
        }

        // Windows drops trailing dots and spaces, so a name ending in them wouldn't stay the same name.
        string suggestion = new string(name).Trim().TrimEnd('.', ' ');
        return suggestion is "" or "." or ".." ? null : suggestion;
    }
}
