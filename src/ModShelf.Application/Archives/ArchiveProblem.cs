namespace ModShelf.Application.Archives;

/// <summary>
/// An identified archive that couldn't be imported, and why, in a sentence.
/// </summary>
/// <param name="Path">Its full path.</param>
public sealed record ArchiveProblem(string Path, string Message)
{
    /// <summary>
    /// Its file name.
    /// </summary>
    public string Name => System.IO.Path.GetFileName(Path);
}
