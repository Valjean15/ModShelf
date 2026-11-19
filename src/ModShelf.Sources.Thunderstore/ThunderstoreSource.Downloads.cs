using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Sources.Thunderstore;

/// <summary>
/// The <see cref="IDownloadResolver"/> half: a version's download link, which redirects to its zip on Thunderstore's CDN
/// and counts the download. Built from the file's IDs, with no request; Thunderstore needs no account and gives no hash.
/// </summary>
public sealed partial class ThunderstoreSource : IDownloadResolver
{
    /// <summary>
    /// Thunderstore's "Install with Mod Manager" hands its versions over as <c>ror2mm://</c> links, though ModShelf never
    /// needs it to: every file downloads straight away.
    /// </summary>
    public bool HandsOverLinks => true;

    /// <returns>A ticket for the version's download link.</returns>
    /// <exception cref="ArgumentException">The file isn't Thunderstore's.</exception>
    public Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
    {
        string version = Version(file);
        Catalog.PackageName package = Package(file.Mod);
        Uri link = new(
            _api.BaseAddress,
            $"package/download/{Uri.EscapeDataString(package.Team)}/{Uri.EscapeDataString(package.Name)}/{Uri.EscapeDataString(version)}/");
        return Task.FromResult<ResolveResult>(new DownloadTicket(link));
    }
}
