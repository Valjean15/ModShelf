namespace ModShelf.Platform.Links;

/// <summary>
/// Reading and changing one key of a <c>mimeapps.list</c> file, such as <c>x-scheme-handler/nxm</c> under
/// <c>[Default Applications]</c>, leaving the rest of the file as it was.
/// </summary>
internal static class MimeAppsList
{
    public const string DefaultApplications = "Default Applications";

    /// <summary>
    /// The key's value, or null when the file, the section or the key is missing.
    /// </summary>
    public static string? Read(string file, string section, string key)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            // Such as a system's file this user can't read: as if it weren't there.
            return null;
        }

        (int start, int end) = Find(lines, section);
        for (int index = start; index < end; index++)
        {
            if (KeyOf(lines[index]) == key)
            {
                return lines[index][(lines[index].IndexOf('=', StringComparison.Ordinal) + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Sets the key to <paramref name="value"/>, or removes it for null. The file and the section are added when
    /// missing.
    /// </summary>
    public static void Write(string file, string section, string key, string? value)
    {
        List<string> lines;
        try
        {
            lines = [.. File.ReadAllLines(file)];
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            if (value is null)
            {
                return;
            }

            lines = [];
        }

        (int start, int end) = Find([.. lines], section);
        int found = Enumerable.Range(start, end - start).FirstOrDefault(index => KeyOf(lines[index]) == key, -1);
        if (value is null)
        {
            if (found < 0)
            {
                return;
            }

            lines.RemoveAt(found);
        }
        else if (found >= 0)
        {
            lines[found] = $"{key}={value}";
        }
        else if (start > 0)
        {
            // After the section's last entry, before the blank lines that end it.
            int at = end;
            while (at > start && string.IsNullOrWhiteSpace(lines[at - 1]))
            {
                at--;
            }

            lines.Insert(at, $"{key}={value}");
        }
        else
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add(string.Empty);
            }

            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temporary = file + ".modshelf-new";
        File.WriteAllText(temporary, string.Join('\n', lines) + "\n");
        File.Move(temporary, file, overwrite: true);
    }

    /// <summary>
    /// The lines of the section's entries, from the one after its header to the next header; (0, 0) when it's missing.
    /// </summary>
    private static (int Start, int End) Find(string[] lines, string section)
    {
        int header = Array.FindIndex(lines, line => line.Trim() == $"[{section}]");
        if (header < 0)
        {
            return (0, 0);
        }

        int next = Array.FindIndex(lines, header + 1, line => line.TrimStart().StartsWith('['));
        return (header + 1, next < 0 ? lines.Length : next);
    }

    private static string? KeyOf(string line)
    {
        int equals = line.IndexOf('=', StringComparison.Ordinal);
        return equals > 0 && !line.TrimStart().StartsWith('#') ? line[..equals].Trim() : null;
    }
}
