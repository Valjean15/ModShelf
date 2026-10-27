using System.Buffers;
using System.Text;

namespace ModShelf.Domain.Library;

/// <summary>
/// Turns names from sites, such as a mod's or a file's, into folder and file names that work on Windows, macOS and
/// Linux alike, so a library can move between them.
/// </summary>
public static class LibraryNames
{
    /// <summary>
    /// The longest folder name, in UTF-8 bytes. Linux allows 255; a shorter limit leaves room for the rest of the path.
    /// </summary>
    public const int MaxFolderBytes = 100;

    /// <summary>
    /// The longest file name, in UTF-8 bytes.
    /// </summary>
    public const int MaxFileBytes = 200;

    // Forbidden by Windows, the strictest, or a path separator anywhere.
    private static readonly SearchValues<char> Forbidden = SearchValues.Create(
        [.. "<>:\"/\\|?*", .. Enumerable.Range(0, 32).Select(code => (char)code)]);

    // Names Windows keeps for devices, with or without an extension; it also treats superscript digits as digits here.
    private static readonly HashSet<string> Reserved = new(
        [
            "CON", "PRN", "AUX", "NUL",
            .. "123456789¹²³".Select(digit => $"COM{digit}"),
            .. "123456789¹²³".Select(digit => $"LPT{digit}"),
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A folder name made from <paramref name="name"/>, at most <see cref="MaxFolderBytes"/> long.
    /// </summary>
    public static string Folder(string name) => Portable(name, MaxFolderBytes, keepExtension: false);

    /// <summary>
    /// A file name made from <paramref name="name"/>, at most <see cref="MaxFileBytes"/> long. When it has to be
    /// shortened, its extension is kept.
    /// </summary>
    public static string File(string name) => Portable(name, MaxFileBytes, keepExtension: true);

    /// <summary>
    /// Characters some system forbids become '_'; leading spaces and trailing spaces and dots, which Windows drops, go;
    /// a name Windows keeps for a device gets a leading '_'; and a name left empty becomes "_".
    /// </summary>
    private static string Portable(string name, int maxBytes, bool keepExtension)
    {
        ArgumentNullException.ThrowIfNull(name);

        Span<char> characters = name.ToCharArray();
        int forbidden;
        while ((forbidden = characters.IndexOfAny(Forbidden)) >= 0)
        {
            characters[forbidden] = '_';
        }

        string portable = Shorten(Trim(new string(characters)), maxBytes, keepExtension);
        if (portable.Length == 0 || portable is "." or "..")
        {
            return "_";
        }

        string stem = portable.Split('.')[0].TrimEnd(' ');
        return Reserved.Contains(stem) ? Shorten("_" + portable, maxBytes, keepExtension) : portable;
    }

    private static string Trim(string name) => name.TrimStart(' ').TrimEnd('.', ' ');

    /// <summary>
    /// Cuts <paramref name="name"/> to at most <paramref name="maxBytes"/> UTF-8 bytes, between characters, keeping a
    /// short extension at the end when asked.
    /// </summary>
    private static string Shorten(string name, int maxBytes, bool keepExtension)
    {
        if (Encoding.UTF8.GetByteCount(name) <= maxBytes)
        {
            return name;
        }

        int dot = name.LastIndexOf('.');
        string extension = keepExtension && dot > 0 && name.Length - dot <= 16 ? name[dot..] : string.Empty;
        string stem = name[..(name.Length - extension.Length)];
        int budget = maxBytes - Encoding.UTF8.GetByteCount(extension);

        StringBuilder kept = new();
        int used = 0;
        foreach (Rune rune in stem.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > budget)
            {
                break;
            }

            kept.Append(rune.ToString());
            used += rune.Utf8SequenceLength;
        }

        return Trim(kept.ToString()) + extension;
    }
}
