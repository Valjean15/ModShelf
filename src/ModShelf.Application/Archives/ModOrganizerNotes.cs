using System.Globalization;
using System.Text;

namespace ModShelf.Application.Archives;

/// <summary>
/// Reads the <c>.meta</c> file Mod Organizer 2 keeps beside each archive it downloads: an INI file Qt's QSettings wrote,
/// whose <c>[General]</c> section names the mod and file on Nexus Mods (ADR 0101).
/// </summary>
/// <remarks>
/// Older versions wrote it in the system's code page, newer ones in UTF-8, and some characters as <c>\x</c> escapes, so
/// it's read leniently: only the keys ModShelf uses matter, and those are digits.
/// </remarks>
internal static class ModOrganizerNotes
{
    /// <summary>
    /// The extension of the note beside an archive, after the archive's own name: <c>SkyUI.7z.meta</c>.
    /// </summary>
    public const string Extension = ".meta";

    /// <summary>
    /// The largest note read: a few hundred bytes is usual, with a mod's description a few kilobytes.
    /// </summary>
    public const int MaxBytes = 256 * 1024;

    // Strict, so a note in the system's code page isn't read as UTF-8 with its accents lost.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// What the note says of a Nexus Mods file.
    /// </summary>
    /// <returns>
    /// Null when it names no mod, or one of another repository than Nexus Mods, which Mod Organizer 2 notes as
    /// <c>Nexus</c>; the file's ID is left out when it names none.
    /// </returns>
    public static ModOrganizerNote? Read(ReadOnlySpan<byte> bytes)
    {
        Dictionary<string, string> general = General(Decode(bytes));
        string? repository = general.GetValueOrDefault("repository");
        if (!string.IsNullOrWhiteSpace(repository) && !string.Equals(repository.Trim(), "Nexus", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Id(general.GetValueOrDefault("modid")) is { } modId
            ? new ModOrganizerNote(modId, Id(general.GetValueOrDefault("fileid")), Blank(general.GetValueOrDefault("modname")) ?? Blank(general.GetValueOrDefault("name")))
            : null;
    }

    /// <summary>
    /// The text, by its byte order mark, as UTF-8 when it's valid UTF-8, or else in Latin-1, which keeps every byte.
    /// </summary>
    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>
    /// The keys and values of the <c>[General]</c> section, keys in lower case; a key given twice, as given last.
    /// </summary>
    private static Dictionary<string, string> General(string text)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool inGeneral = false;
        foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                inGeneral = string.Equals(line, "[General]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            int equals = line.IndexOf('=', StringComparison.Ordinal);
            if (inGeneral && equals > 0)
            {
                values[line[..equals].Trim().ToLowerInvariant()] = Unescape(line[(equals + 1)..].Trim());
            }
        }

        return values;
    }

    /// <summary>
    /// A value as QSettings wrote it: in quotes when it holds a separator, with <c>\"</c>, <c>\\</c> and <c>\xHHHH</c>
    /// escapes.
    /// </summary>
    private static string Unescape(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        StringBuilder text = new(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character != '\\' || index == value.Length - 1)
            {
                text.Append(character);
                continue;
            }

            char next = value[++index];
            if (next is 'x' or 'X')
            {
                int digits = 0;
                while (digits < 4 && index + 1 + digits < value.Length && char.IsAsciiHexDigit(value[index + 1 + digits]))
                {
                    digits++;
                }

                if (digits > 0)
                {
                    text.Append((char)int.Parse(value.AsSpan(index + 1, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    index += digits;
                    continue;
                }
            }

            text.Append(next switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                _ => next,
            });
        }

        return text.ToString();
    }

    /// <summary>
    /// An ID of digits greater than zero, as Mod Organizer 2 notes 0 for none; null otherwise.
    /// </summary>
    private static string? Id(string? value)
        => value is not null && long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
