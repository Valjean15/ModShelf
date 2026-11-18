using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Lookup;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="IFileLookup"/> half: a game's files by their MD5, through the v1 API's search, as Vortex identifies
/// the archives it's given (ADR 0101).
/// </summary>
public sealed partial class NexusSource : IFileLookup
{
    public FileHashAlgorithm LookupAlgorithm => FileHashAlgorithm.Md5;

    /// <summary>
    /// A GET of <c>v1/games/{game}/mods/md5_search/{md5}.json</c>: one request. Nexus Mods answers 404 for an MD5 it
    /// doesn't know, which is no file.
    /// </summary>
    /// <returns>
    /// Each file it lists of the game, with its mod and the MD5 it gives; one listed twice, once. A file or a mod the API
    /// leaves incomplete, or of another game, is left out.
    /// </returns>
    /// <exception cref="ArgumentException">The game isn't Nexus Mods', or the hash isn't an MD5.</exception>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the API refused it.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached, or refused.</exception>
    public async Task<IReadOnlyList<FoundFile>> FindByHashAsync(GameRef game, FileHash hash, CancellationToken cancellationToken)
    {
        string domain = NexusLink.ForGame(game);
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Algorithm != FileHashAlgorithm.Md5)
        {
            throw new ArgumentException($"Nexus Mods finds files by MD5, not {hash.Algorithm}.", nameof(hash));
        }

        NexusHashMatch?[] matches;
        try
        {
            matches = await _api.GetAsync<NexusHashMatch?[]>($"{NexusLink.GameApiPath(domain)}/mods/md5_search/{hash.Value}.json", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        List<FoundFile> found = [];
        HashSet<ModFileRef> listed = [];
        foreach (NexusHashMatch? match in matches)
        {
            if (match is not { Mod: { } mod, FileDetails: { } file }
                || NexusLink.FromApi(mod.DomainName ?? domain, mod.ModId) is not { } link
                || link.Game != domain
                || file.FileId <= 0)
            {
                continue;
            }

            ModRef modRef = new(game, link.ModId);
            FileHash said = file.Md5 is { Length: 32 } md5 && md5.All(char.IsAsciiHexDigit) ? new FileHash(FileHashAlgorithm.Md5, md5) : hash;
            ModFile modFile = ToModFile(modRef, link, file, replacedBy: null, hashes: [said]);
            if (listed.Add(modFile.Reference))
            {
                found.Add(new FoundFile(ToModInfo(modRef, link, mod, category: null), modFile));
            }
        }

        return found;
    }
}
