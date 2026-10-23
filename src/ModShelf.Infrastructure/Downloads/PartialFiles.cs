namespace ModShelf.Infrastructure.Downloads;

/// <summary>
/// Where a download keeps its bytes until the file is complete and verified.
/// </summary>
internal static class PartialFiles
{
    public const string Extension = ".part";

    /// <summary>
    /// The partial file for <paramref name="destinationPath"/>: same folder, so the final move is a rename.
    /// </summary>
    public static string PathFor(string destinationPath) => destinationPath + Extension;

    public static long LengthOf(string partialPath)
        => File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
}
