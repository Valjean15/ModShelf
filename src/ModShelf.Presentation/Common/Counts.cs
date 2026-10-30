namespace ModShelf.Presentation.Common;

/// <summary>
/// Writes a count with the words for it: one wording for exactly one, another the count is written into.
/// </summary>
internal static class Counts
{
    /// <summary>
    /// <paramref name="one"/> for 1; otherwise <paramref name="many"/>, such as "{0:N0} downloads", with the count.
    /// </summary>
    public static string Of(long count, string one, string many) => count == 1 ? one : Text.Format(many, count);

    /// <summary>
    /// <paramref name="none"/> for 0, <paramref name="one"/> for 1; otherwise <paramref name="many"/> with the count.
    /// </summary>
    public static string Of(long count, string none, string one, string many) => count == 0 ? none : Of(count, one, many);
}
