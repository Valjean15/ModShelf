using System.Diagnostics.CodeAnalysis;

namespace ModShelf.Domain.Mods;

/// <summary>
/// A game as one source knows it: the source, plus that source's own key for the game,
/// such as Nexus Mods' <c>skyrimspecialedition</c>.
/// </summary>
/// <remarks>
/// As text, as the settings and the CLI name it, the site comes first: <c>nexus/skyrimspecialedition</c>.
/// </remarks>
public sealed record GameRef
{
    public GameRef(SourceId source, string key)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Source = source;
        Key = key;
    }

    public SourceId Source { get; }

    public string Key { get; }

    /// <summary>
    /// The game named as text, such as <c>nexus/stardewvalley</c>: its site's ID, then its key. The key may hold slashes
    /// too; the site's ID never does.
    /// </summary>
    /// <returns>False for text without both, or with a site's ID that can't be one.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out GameRef? game)
    {
        game = null;
        int slash = text?.IndexOf('/', StringComparison.Ordinal) ?? -1;
        if (text is null || slash < 0)
        {
            return false;
        }

        string site = text[..slash].Trim();
        string key = text[(slash + 1)..].Trim();
        if (site.Length == 0 || key.Length == 0)
        {
            return false;
        }

        try
        {
            game = new GameRef(new SourceId(site), key);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The game as text, such as <c>nexus/stardewvalley</c>, which <see cref="TryParse"/> reads back.
    /// </summary>
    public override string ToString() => $"{Source.Value}/{Key}";
}
