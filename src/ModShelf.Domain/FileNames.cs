using System.Buffers;
using System.Runtime.CompilerServices;

namespace ModShelf.Domain;

/// <summary>
/// Rules for the file names that sources supply.
/// </summary>
internal static class FileNames
{
    private static readonly SearchValues<char> InvalidCharacters = SearchValues.Create(Path.GetInvalidFileNameChars());

    /// <summary>
    /// Throws unless <paramref name="fileName"/> is a single path segment: not blank, free of the characters
    /// the current OS forbids in file names (which include directory separators), and not "." or "..".
    /// A name from a source therefore can't point outside the folder it is saved to.
    /// </summary>
    /// <remarks>
    /// This is the safety check, not sanitizing. Making names portable (Windows reserved names, trailing
    /// dots, length limits) belongs to the storage rules.
    /// </remarks>
    public static void EnsureSingleSegment(
        string fileName,
        [CallerArgumentExpression(nameof(fileName))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName, paramName);
        if (fileName is "." or ".." || fileName.AsSpan().ContainsAny(InvalidCharacters))
        {
            throw new ArgumentException($"'{fileName}' is not a valid file name.", paramName);
        }
    }
}
