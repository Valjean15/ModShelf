using System.Diagnostics;
using System.Text;

namespace ModShelf.Platform.Links;

/// <summary>
/// Windows command lines, as the registry keeps them for opening links: building one, and naming the app one starts.
/// </summary>
internal static class WindowsCommandLine
{
    /// <summary>
    /// <paramref name="argument"/> quoted so that a program reading its command line the usual way
    /// (<c>CommandLineToArgvW</c>) reads it back as it is.
    /// </summary>
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        StringBuilder quoted = new("\"");
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes before a quote are doubled, and the quote escaped; elsewhere they stand as they are.
            quoted.Append('\\', character == '"' ? (backslashes * 2) + 1 : backslashes).Append(character);
            backslashes = 0;
        }

        // Before the closing quote, they're doubled too.
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    /// <summary>
    /// The program a command line starts: its first argument, quoted or not.
    /// </summary>
    public static string? Program(string? command)
    {
        string text = command?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        if (text[0] == '"')
        {
            int end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        // Unquoted, as in C:\Programs\Vortex\Vortex.exe -d "%1": up to the first space after the program's name.
        int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        int space = text.IndexOf(' ', StringComparison.Ordinal);
        return exe > 0 ? text[..(exe + 4)] : space > 0 ? text[..space] : text;
    }

    /// <summary>
    /// The name of the app a command line starts, such as <c>Vortex</c>: its program's description, when the program
    /// is there and has one, otherwise its file name without the extension.
    /// </summary>
    public static string? AppName(string? command)
    {
        if (Program(command) is not { } program)
        {
            return null;
        }

        string? description = null;
        try
        {
            description = File.Exists(program) ? FileVersionInfo.GetVersionInfo(program).FileDescription?.Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Unreadable: named by its file instead.
        }

        if (!string.IsNullOrEmpty(description))
        {
            return description;
        }

        // A Windows path, split at its backslashes on any system, since the registry is Windows'.
        string file = program[(program.LastIndexOfAny(['\\', '/']) + 1)..];
        int dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }
}
