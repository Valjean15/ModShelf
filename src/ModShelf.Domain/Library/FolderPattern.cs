using System.Text.RegularExpressions;

namespace ModShelf.Domain.Library;

/// <summary>
/// Where a downloaded file goes in the library, as a pattern of folders, such as
/// <c>{source}/{game}/{mod} ({modid})/{file}</c>.
/// </summary>
/// <remarks>
/// Folders are separated by '/', and the last part is the file itself: exactly <c>{file}</c>. The other parts mix text
/// with <c>{source}</c> (the site's ID, such as nexus), <c>{game}</c> (the game as the site names it in links, such as
/// stardewvalley), <c>{mod}</c> (the mod's name) and <c>{modid}</c> (the mod's ID on the site).
/// </remarks>
public sealed partial record FolderPattern
{
    public const string SourceToken = "{source}";
    public const string GameToken = "{game}";
    public const string ModToken = "{mod}";
    public const string ModIdToken = "{modid}";
    public const string FileToken = "{file}";

    private static readonly string[] FolderTokens = [SourceToken, GameToken, ModToken, ModIdToken];

    private FolderPattern(string text)
    {
        Text = text;
    }

    /// <summary>
    /// Mod folders named after the mod and its ID, under the site and the game.
    /// </summary>
    public static FolderPattern Default { get; } = Parse($"{SourceToken}/{GameToken}/{ModToken} ({ModIdToken})/{FileToken}");

    public string Text { get; }

    /// <exception cref="ArgumentException">
    /// The pattern isn't relative, has an empty or unknown part, doesn't end with <c>/{file}</c>, or has text some system
    /// forbids in a name. The message says which.
    /// </exception>
    public static FolderPattern Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Contains('\\', StringComparison.Ordinal))
        {
            throw Invalid(text, "Separate folders with '/'.");
        }

        string[] parts = text.Split(LibraryPaths.Separator);
        if (parts[^1] != FileToken)
        {
            throw Invalid(text, $"The last part must be exactly {FileToken}.");
        }

        foreach (string part in parts[..^1])
        {
            if (part.Length == 0)
            {
                throw Invalid(text, "It has an empty folder; it can't start with '/', end with it, or have two in a row.");
            }

            foreach (Match token in Token().Matches(part))
            {
                if (!FolderTokens.Contains(token.Value))
                {
                    throw Invalid(text, $"{token.Value} isn't a folder's part. Use {string.Join(", ", FolderTokens)}; {FileToken} only names the file, at the end.");
                }
            }

            // With every part filled in, the folder must already be a name every system takes.
            string sample = FolderTokens.Aggregate(part, (filled, folderToken) => filled.Replace(folderToken, "x", StringComparison.Ordinal));
            if (sample.Contains('{', StringComparison.Ordinal) || sample.Contains('}', StringComparison.Ordinal))
            {
                throw Invalid(text, "It has a '{' or '}' that isn't part of a name in braces, such as {mod}.");
            }

            if (LibraryNames.Folder(sample) != sample)
            {
                throw Invalid(text, $"'{part}' isn't a folder name every system takes: no <>:\"\\|?*, no name such as CON, and no space or dot at its end.");
            }
        }

        return new FolderPattern(text);
    }

    /// <summary>
    /// Where a file goes, relative to the library root: the pattern filled in with names made safe for every system.
    /// </summary>
    /// <param name="source">The site's ID, such as nexus.</param>
    /// <param name="game">The game as the site names it in links.</param>
    /// <param name="mod">The mod's name.</param>
    /// <param name="modId">The mod's ID on the site.</param>
    /// <param name="fileName">The file's name, as the site gives it.</param>
    public string Render(string source, string game, string mod, string modId, string fileName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(modId);
        ArgumentNullException.ThrowIfNull(fileName);

        // In one pass, so a name that looks like a part, such as a mod called "{modid}", stays as it is.
        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            [SourceToken] = source,
            [GameToken] = game,
            [ModToken] = mod,
            [ModIdToken] = modId,
        };
        string[] parts = Text.Split(LibraryPaths.Separator);
        IEnumerable<string> folders = parts[..^1].Select(part => LibraryNames.Folder(Token().Replace(part, token => values[token.Value])));
        return string.Join(LibraryPaths.Separator, [.. folders, LibraryNames.File(fileName)]);
    }

    public override string ToString() => Text;

    private static ArgumentException Invalid(string text, string problem)
        => new($"'{text}' isn't a folder pattern. {problem}", nameof(text));

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Token();
}
