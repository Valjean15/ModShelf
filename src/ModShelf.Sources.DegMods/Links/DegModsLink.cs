using System.Globalization;

namespace ModShelf.Sources.DegMods.Links;

/// <summary>
/// DEG Mods' links to a mod: its page, <c>degmods.com/mod/&lt;naddr&gt;</c>, and its address as a <c>nostr:</c> link; and
/// ModShelf's own link to one of its files, the mod's page with <c>#file=&lt;id&gt;</c>, since DEG Mods gives files no
/// page. Opened in a browser, a file's link shows its mod's page.
/// </summary>
internal sealed record DegModsLink(int Kind, string PublicKey, string D, string? FileId)
{
    /// <summary>
    /// Mods since Aug 1, 2026.
    /// </summary>
    public const int ModKind = 31142;

    /// <summary>
    /// Mods before then, NIP-99 listings tagged <c>t=GameMod</c>.
    /// </summary>
    public const int OldModKind = 30402;

    private const string NostrScheme = "nostr";
    private const string FilePrefix = "file=";

    /// <summary>
    /// The mod a link is to, and the file when it names one; null when it isn't DEG Mods'.
    /// </summary>
    /// <param name="site">DEG Mods' address, such as <c>https://degmods.com/</c>.</param>
    /// <exception cref="ArgumentException">The link is DEG Mods', but not to a mod, or to a file that can't be one.</exception>
    public static DegModsLink? Read(Uri link, Uri site)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(site);
        if (!link.IsAbsoluteUri)
        {
            return null;
        }

        if (link.Scheme == NostrScheme)
        {
            // Nostr links name anything; only a mod's address is DEG Mods'.
            return Nip19.TryReadNaddr(link.OriginalString[(NostrScheme.Length + 1)..], out (int Kind, string PublicKey, string D) nostr) && IsMod(nostr.Kind)
                ? new DegModsLink(nostr.Kind, nostr.PublicKey, nostr.D, FileId: null)
                : null;
        }

        if ((link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp) || !IsSite(link, site))
        {
            return null;
        }

        string[] parts = link.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not ["mod", string naddr] || !Nip19.TryReadNaddr(Uri.UnescapeDataString(naddr), out (int Kind, string PublicKey, string D) address))
        {
            throw new ArgumentException("This DEG Mods link isn't to a mod. Copy the link of the mod's page.", nameof(link));
        }

        if (!IsMod(address.Kind))
        {
            throw new ArgumentException("This DEG Mods link is to something other than a mod, such as a mod jam.", nameof(link));
        }

        string fragment = link.Fragment.TrimStart('#');
        string? file = null;
        if (fragment.StartsWith(FilePrefix, StringComparison.Ordinal))
        {
            file = fragment[FilePrefix.Length..];
            if (!IsFileId(file))
            {
                throw new ArgumentException("This DEG Mods link names a file ModShelf doesn't know. Copy the link of the mod's page.", nameof(link));
            }
        }

        return new DegModsLink(address.Kind, address.PublicKey, address.D, file);
    }

    /// <summary>
    /// The mod's page on DEG Mods.
    /// </summary>
    public static Uri ModPage(Uri site, int kind, string publicKey, string d) => new(site, $"mod/{Nip19.Naddr(kind, publicKey, d)}");

    /// <summary>
    /// ModShelf's link to the mod's file: its page, naming the file.
    /// </summary>
    public static Uri FilePage(Uri site, int kind, string publicKey, string d, string fileId)
        => new($"{ModPage(site, kind, publicKey, d)}#{FilePrefix}{fileId}");

    /// <summary>
    /// The game's page on DEG Mods, by its name.
    /// </summary>
    public static Uri GamePage(Uri site, string name) => new(site, $"game/{Uri.EscapeDataString(name)}");

    /// <summary>
    /// Whether a file's ID is one ModShelf gives: the first 16 hexadecimal digits of its SHA-256.
    /// </summary>
    public static bool IsFileId(string? id)
        => id is { Length: 16 } && id.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    /// <summary>
    /// The ID ModShelf gives a file with that SHA-256, in hexadecimal.
    /// </summary>
    public static string FileIdOf(string sha256) => sha256[..16].ToLower(CultureInfo.InvariantCulture);

    private static bool IsMod(int kind) => kind is ModKind or OldModKind;

    private static bool IsSite(Uri link, Uri site)
        => link.Host.Equals(site.Host, StringComparison.OrdinalIgnoreCase)
            || link.Host.Equals($"www.{site.Host}", StringComparison.OrdinalIgnoreCase);
}
