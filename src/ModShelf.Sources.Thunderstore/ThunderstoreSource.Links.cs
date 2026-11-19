using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.Thunderstore.Api;
using ModShelf.Sources.Thunderstore.Catalog;
using ModShelf.Sources.Thunderstore.Links;

namespace ModShelf.Sources.Thunderstore;

/// <summary>
/// The <see cref="ILinkRecognizer"/> and <see cref="IHandoffScheme"/> halves: links to packages and versions on
/// Thunderstore's website, and its <c>ror2mm://</c> links (<see cref="ThunderstoreLink"/>). A link in a community is
/// looked up in its listing, for the package's name as Thunderstore writes it and, for a package's page, its newest
/// version. A link that names no community has its package's communities asked of Thunderstore first: one that's listed
/// in several can't be told apart, so it's refused, asking for its page's link.
/// </summary>
public sealed partial class ThunderstoreSource : ILinkRecognizer, IHandoffScheme
{
    // How many communities a refusal names, of a package listed in more.
    private const int MostCommunitiesNamed = 3;

    /// <summary>
    /// <c>ror2mm</c>, which Thunderstore's "Install with Mod Manager" opens.
    /// </summary>
    public string Scheme => ThunderstoreLink.Scheme;

    /// <returns>The version the link is to, or the package's newest; null for a link that isn't Thunderstore's.</returns>
    /// <exception cref="ArgumentException">
    /// The link is Thunderstore's but not to a package or a version, or is to a package listed in several communities
    /// that names none.
    /// </exception>
    /// <exception cref="HttpRequestException">
    /// The community or package doesn't exist, or doesn't list the version (404), or Thunderstore couldn't be reached.
    /// </exception>
    public async Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        if (ThunderstoreLink.Parse(link) is not { } parsed)
        {
            return null;
        }

        string community = parsed.Community ?? await CommunityOfAsync(parsed.Package, cancellationToken).ConfigureAwait(false);
        Listing listing = await _listings.GetAsync(community, cancellationToken).ConfigureAwait(false);
        ListedPackage package = listing.Find(parsed.Package.FullName)
            ?? throw new HttpRequestException(
                $"Thunderstore doesn't list {parsed.Package.FullName} in {community}.",
                null,
                HttpStatusCode.NotFound);
        ListedVersion version = parsed.Version is null
            ? package.Newest
            : package.Version(parsed.Version)
                ?? throw new HttpRequestException(
                    $"Thunderstore doesn't list version {parsed.Version} of {package.Name.FullName}: its author may have taken it down.",
                    null,
                    HttpStatusCode.NotFound);

        ModFileRef file = new(ModOf(community, package.Name), version.Number);
        return new RecognizedLink(file, ThunderstoreFiles.FileName(package.Name, version.Number), isHandoff: parsed.IsHandoff);
    }

    /// <summary>
    /// The one community that lists <paramref name="package"/>, asked of Thunderstore.
    /// </summary>
    private async Task<string> CommunityOfAsync(PackageName package, CancellationToken cancellationToken)
    {
        ThunderstorePackageDetail detail = await _api.GetAsync<ThunderstorePackageDetail>(
                $"api/experimental/package/{Uri.EscapeDataString(package.Team)}/{Uri.EscapeDataString(package.Name)}/",
                cancellationToken)
            .ConfigureAwait(false);
        string[] communities = [.. (detail.CommunityListings ?? [])
            .Select(listing => listing.Community)
            .Where(IsCommunity)
            .Distinct(StringComparer.Ordinal)];
        return communities switch
        {
            [var only] => only,
            [] => throw new HttpRequestException($"No community on Thunderstore lists {package.FullName}.", null, HttpStatusCode.NotFound),
            _ => throw new ArgumentException(
                $"{package.FullName} is listed for {communities.Length} games on Thunderstore, such as "
                + $"{string.Join(", ", communities.Take(MostCommunitiesNamed))}, and the link doesn't say which. Use the link of its page "
                + $"in the game's community, such as {ThunderstoreLink.Page(communities[0], package)}."),
        };
    }
}
