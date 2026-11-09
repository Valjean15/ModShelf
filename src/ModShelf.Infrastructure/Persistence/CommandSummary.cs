using System.Text.RegularExpressions;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// What a database command does, in a few words to log, such as <c>SELECT from Mods</c>: its statement and the first
/// table it names, never its values.
/// </summary>
internal static partial class CommandSummary
{
    /// <summary>
    /// Such as <c>SELECT from Mods</c>, <c>INSERT into DownloadJobs</c> or <c>UPDATE Mods</c>; the statement alone when it
    /// names no table, such as <c>PRAGMA</c>.
    /// </summary>
    public static string Of(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || Statement().Match(sql) is not { Success: true } statement)
        {
            return "a command";
        }

        string verb = statement.Groups["verb"].Value.ToUpperInvariant();
        if (Table().Match(sql) is not { Success: true } table)
        {
            return verb;
        }

        string keyword = table.Groups["keyword"].Value.ToUpperInvariant();
        string name = table.Groups["name"].Value.Trim('"');
        return keyword == verb ? $"{verb} {name}" : $"{verb} {keyword.ToLowerInvariant()} {name}";
    }

    [GeneratedRegex(@"^\s*(?<verb>[A-Za-z]+)")]
    private static partial Regex Statement();

    // An identifier alone, quoted or not, so nothing in the command's text but a table's name is kept.
    [GeneratedRegex(@"\b(?<keyword>FROM|INTO|UPDATE|TABLE)\s+(?<name>""[A-Za-z_][A-Za-z0-9_]*""|[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Table();
}
