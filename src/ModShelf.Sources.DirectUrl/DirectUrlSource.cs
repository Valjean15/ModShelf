using System.Buffers;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.DirectUrl.Resources;

namespace ModShelf.Sources.DirectUrl;

/// <summary>
/// Downloads files from plain web links, with no mod site involved. It recognizes every http and https
/// link as a catch-all match, so a site-specific source wins whenever it recognizes the link too.
/// </summary>
/// <remarks>
/// <para>
/// Nothing but the link identifies the file, so the link is the file's ID: it's saved with the job, which
/// lets a download resume after a restart. Links with a user name or password are refused for that reason,
/// since credentials must never reach the database. Signed links that expire can't resume once they have.
/// </para>
/// <para>
/// A link can carry the file's expected hash as a pip-style fragment, such as <c>#sha256=…</c>, with
/// <c>md5</c>, <c>sha1</c>, <c>sha256</c> or <c>sha512</c>. Fragments are never sent to the server.
/// </para>
/// </remarks>
public sealed class DirectUrlSource : IDownloadResolver, ILinkRecognizer
{
    // Direct links aren't tied to a game, and the closest thing to a mod is the site serving the file.
    private const string GameKey = "web";

    private static readonly SourceId DirectId = new("direct");

    // The characters any of the supported systems forbid in file names, so a downloaded file can move between them.
    private static readonly SearchValues<char> ForbiddenInFileNames = SearchValues.Create(
        [.. Path.GetInvalidFileNameChars(), .. "<>:\"/\\|?*", .. Enumerable.Range(0, 32).Select(code => (char)code)]);

    public SourceId Id => DirectId;

    public string DisplayName => Strings.DisplayName;

    public Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!IsWebLink(link))
        {
            return Task.FromResult<RecognizedLink?>(null);
        }

        _ = ReadExpectedHash(link);
        ModFileRef file = new(new ModRef(new GameRef(DirectId, GameKey), link.IdnHost), link.AbsoluteUri);
        return Task.FromResult<RecognizedLink?>(new RecognizedLink(file, SuggestFileName(link), isCatchAll: true));
    }

    /// <param name="handoff">Ignored: a direct link never asks the user to start the download on a website.</param>
    public Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Mod.Game.Source != DirectId)
        {
            throw new ArgumentException($"The file belongs to source '{file.Mod.Game.Source}', not '{DirectId}'.", nameof(file));
        }

        if (!Uri.TryCreate(file.Id, UriKind.Absolute, out Uri? link) || !IsWebLink(link))
        {
            throw new ArgumentException("The file's ID isn't a web link.", nameof(file));
        }

        FileHash? expectedHash = ReadExpectedHash(link);
        Uri withoutFragment = new(link.GetLeftPart(UriPartial.Query));
        return Task.FromResult<ResolveResult>(new DownloadTicket(withoutFragment, expectedHash: expectedHash));
    }

    private static bool IsWebLink(Uri link)
        => link.IsAbsoluteUri && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp);

    /// <summary>
    /// Checks that the link can be saved and reads the expected hash from its fragment, if it has one.
    /// </summary>
    private static FileHash? ReadExpectedHash(Uri link)
    {
        if (link.UserInfo.Length > 0)
        {
            throw new ArgumentException(
                "Links with a user name or password aren't supported, because the link is saved with the download.",
                nameof(link));
        }

        FileHash? hash = null;
        foreach (string part in link.Fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=', StringComparison.Ordinal);
            FileHashAlgorithm? algorithm = equals < 0 ? null : AlgorithmNamed(part[..equals]);
            if (algorithm is null)
            {
                continue;
            }

            if (hash is not null)
            {
                throw new ArgumentException("The link gives more than one hash; give only one.", nameof(link));
            }

            hash = new FileHash(algorithm.Value, part[(equals + 1)..]);
        }

        return hash;
    }

    private static FileHashAlgorithm? AlgorithmNamed(string name) => name.ToUpperInvariant() switch
    {
        "MD5" => FileHashAlgorithm.Md5,
        "SHA1" => FileHashAlgorithm.Sha1,
        "SHA256" => FileHashAlgorithm.Sha256,
        "SHA512" => FileHashAlgorithm.Sha512,
        _ => null,
    };

    /// <summary>
    /// The link's last path segment, unescaped, with forbidden characters replaced by '_'. Null for a link
    /// to a folder or a site, which names no file.
    /// </summary>
    private static string? SuggestFileName(Uri link)
    {
        string segment = link.Segments[^1];
        if (segment.EndsWith('/'))
        {
            return null;
        }

        Span<char> name = Uri.UnescapeDataString(segment).ToCharArray();
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
