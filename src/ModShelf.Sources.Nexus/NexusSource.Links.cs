using System.Globalization;

using ModShelf.Domain.Library;
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

        NexusFile file = nexus.FileId is not null
            ? await _api.GetAsync<NexusFile>($"{nexus.FileApiPath}.json", cancellationToken).ConfigureAwait(false)
            : NewestMainFile(await _api.GetAsync<NexusModFiles>($"{nexus.ModApiPath}/files.json", cancellationToken).ConfigureAwait(false));

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
    /// The name Nexus Mods gives the file, made to work on every system as the library's names are. Null when it gives
    /// none, or only dots and spaces.
    /// </summary>
    private static string? SuggestFileName(string? fileName)
        => string.IsNullOrWhiteSpace(fileName?.Replace('.', ' ')) ? null : LibraryNames.File(fileName.Trim());
}
