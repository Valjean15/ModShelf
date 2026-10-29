using System.Text.Json;

namespace ModShelf.Infrastructure.Downloads;

/// <summary>
/// Where a download keeps its bytes until the file is complete and verified, and what the server said of the file
/// beside them.
/// </summary>
internal static class PartialFiles
{
    public const string Extension = ".part";

    private const string RecordExtension = ".info";

    /// <summary>
    /// The partial file for <paramref name="destinationPath"/>: same folder, so the final move is a rename.
    /// </summary>
    public static string PathFor(string destinationPath) => destinationPath + Extension;

    public static long LengthOf(string partialPath)
        => File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

    /// <summary>
    /// What the server said of the file <paramref name="partialPath"/> holds the start of; or null when nothing was kept,
    /// as for a partial file from before ModShelf kept it, or it can't be read.
    /// </summary>
    public static PartialFileRecord? ReadRecord(string partialPath)
    {
        try
        {
            return JsonSerializer.Deserialize<PartialFileRecord>(File.ReadAllText(RecordPathFor(partialPath)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <exception cref="IOException">The record can't be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder can't be written.</exception>
    public static void WriteRecord(string partialPath, PartialFileRecord record)
        => File.WriteAllText(RecordPathFor(partialPath), JsonSerializer.Serialize(record));

    /// <summary>
    /// Deletes the record kept beside <paramref name="partialPath"/>, if there is one. A record left behind is harmless:
    /// the next download of the file replaces it.
    /// </summary>
    public static void DeleteRecord(string partialPath)
    {
        try
        {
            File.Delete(RecordPathFor(partialPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the next download of the file to replace.
        }
    }

    private static string RecordPathFor(string partialPath) => partialPath + RecordExtension;
}
