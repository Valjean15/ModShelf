using System.Globalization;
using System.Text.RegularExpressions;

namespace ModShelf.Sources.ModIo.Links;

/// <summary>
/// A link to a mod on mod.io, or one of its files: its page, <c>mod.io/g/{game}/m/{mod}</c>, which names both by the
/// names in their links; or its API's link, <c>…/v1/games/{game}/mods/{mod}/files/{file}/download</c> on one of mod.io's
/// API hosts, which names all three by their IDs.
/// </summary>
/// <param name="GameName">The game's name in links, such as <c>baldursgate3</c>, for a page; null otherwise.</param>
/// <param name="ModName">The mod's name in links, such as <c>better-inventory-ui1</c>, for a page; null otherwise.</param>
/// <param name="GameId">The game's ID, for an API link; null for a page.</param>
/// <param name="ModId">The mod's ID, for an API link; null for a page.</param>
/// <param name="FileId">The file's ID, when the link names one.</param>
internal sealed partial record ModIoLink(string? GameName, string? ModName, long? GameId, long? ModId, long? FileId)
{
    private const string NotAMod = "That mod.io link isn't to a mod: copy the link of a mod's page.";

    /// <summary>
    /// The link to download a file by, as mod.io gives it: on the game's API host, naming the game, the mod and the file.
    /// </summary>
    public static Uri FileLink(long game, long mod, long file)
        => new(string.Create(CultureInfo.InvariantCulture, $"https://g-{game}.modapi.io/v1/games/{game}/mods/{mod}/files/{file}/download"));

    /// <summary>
    /// What the link is to, or null when it isn't mod.io's: its website, or an API host.
    /// </summary>
    /// <exception cref="ArgumentException">The link is mod.io's, but to something other than a mod or a file of one.</exception>
    public static ModIoLink? Read(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!link.IsAbsoluteUri || (link.Scheme != Uri.UriSchemeHttps && link.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        // The host as Uri writes it, in lowercase.
        string host = link.Host;
        string[] path = link.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (host is "mod.io" or "www.mod.io")
        {
            return path switch
            {
                ["g", var game, "m", var mod, ..] when IsName(game) && IsName(mod)
                    => new ModIoLink(game.ToLowerInvariant(), mod.ToLowerInvariant(), null, null, null),
                _ => throw new ArgumentException(NotAMod, nameof(link)),
            };
        }

        if (host == "api.mod.io" || ApiHost().IsMatch(host))
        {
            return path switch
            {
                ["v1", "games", var game, "mods", var mod] when IsId(game) && IsId(mod)
                    => new ModIoLink(null, null, Id(game), Id(mod), null),
                ["v1", "games", var game, "mods", var mod, "files", var file, .. var rest]
                    when IsId(game) && IsId(mod) && IsId(file) && rest is [] or ["download"]
                    => new ModIoLink(null, null, Id(game), Id(mod), Id(file)),
                _ => throw new ArgumentException(NotAMod, nameof(link)),
            };
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="text"/> is one of mod.io's IDs: a positive number, as its games, mods and files have.
    /// </summary>
    public static bool IsId(string? text)
        => text is { Length: > 0 and <= 18 } && text.All(char.IsAsciiDigit) && long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) > 0;

    /// <summary>
    /// The ID written as <paramref name="text"/>, which <see cref="IsId"/> takes.
    /// </summary>
    public static long Id(string text) => long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether <paramref name="text"/> could be a game's or a mod's name in mod.io's links: letters, digits, hyphens and
    /// underscores.
    /// </summary>
    private static bool IsName(string text) => NameShape().IsMatch(text);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9_-]{0,99}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NameShape();

    /// <summary>
    /// A game's or a user's own API host, such as <c>g-6715.modapi.io</c>.
    /// </summary>
    [GeneratedRegex(@"^[gu]-[0-9]{1,18}\.modapi\.io$", RegexOptions.CultureInvariant)]
    private static partial Regex ApiHost();
}
