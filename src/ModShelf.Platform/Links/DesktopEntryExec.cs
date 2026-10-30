using System.Buffers;
using System.Text;

namespace ModShelf.Platform.Links;

/// <summary>
/// The <c>Exec</c> key of a desktop entry, as the freedesktop.org Desktop Entry specification quotes it.
/// </summary>
internal static class DesktopEntryExec
{
    // The characters an argument can't hold unless it's quoted.
    private static readonly SearchValues<char> Reserved = SearchValues.Create(" \t\n\"'\\><~|&;$*?#()`");

    /// <summary>
    /// The value that starts <paramref name="program"/> with <paramref name="arguments"/>, then the link (<c>%u</c>).
    /// A <c>%</c> is doubled. An argument with a reserved character, such as a space, is quoted: within the quotes
    /// <c>"</c>, <c>`</c>, <c>$</c> and <c>\</c> are escaped, and the file's own escaping then doubles each backslash
    /// again. Other arguments stay as they are, since <c>xdg-open</c> outside a desktop environment doesn't read quotes.
    /// </summary>
    public static string ForLink(string program, IEnumerable<string> arguments)
        => string.Join(' ', [Quote(program), .. arguments.Select(Quote), "%u"]);

    private static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.AsSpan().ContainsAny(Reserved))
        {
            return argument.Replace("%", "%%", StringComparison.Ordinal);
        }

        StringBuilder quoted = new("\"");
        foreach (char character in argument)
        {
            if (character is '"' or '`' or '$' or '\\')
            {
                quoted.Append('\\');
            }

            quoted.Append(character == '%' ? "%%" : character.ToString());
        }

        return quoted.Append('"').ToString().Replace("\\", "\\\\", StringComparison.Ordinal);
    }
}
