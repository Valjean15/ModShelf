namespace ModShelf.Application.Contents;

/// <summary>
/// Why an archive, or a file in it, couldn't be read.
/// </summary>
public enum ArchiveTrouble
{
    /// <summary>
    /// It isn't an archive ModShelf reads: a .zip, .7z or .rar.
    /// </summary>
    NotAnArchive,

    /// <summary>
    /// It needs a password: its list of files, or the file asked for.
    /// </summary>
    Encrypted,

    /// <summary>
    /// It's damaged or cut short, or isn't what its name says.
    /// </summary>
    Broken,

    /// <summary>
    /// The file asked for isn't text.
    /// </summary>
    NotText,
}
