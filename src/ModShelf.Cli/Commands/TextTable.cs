namespace ModShelf.Cli.Commands;

/// <summary>
/// Writes rows as columns that line up, for the list commands. The first row holds the headings.
/// </summary>
internal static class TextTable
{
    private const string Gap = "  ";

    public static void Write(TextWriter output, IReadOnlyList<string[]> rows)
    {
        int[] widths = [.. Enumerable.Range(0, rows[0].Length).Select(column => rows.Max(row => row[column].Length))];
        foreach (string[] row in rows)
        {
            output.WriteLine(string.Join(Gap, row.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd());
        }
    }
}
