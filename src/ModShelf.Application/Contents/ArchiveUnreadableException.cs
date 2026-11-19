namespace ModShelf.Application.Contents;

/// <summary>
/// An archive, or a file in it, that can't be read, saying why. Reading it again wouldn't help: nothing is retried.
/// </summary>
public sealed class ArchiveUnreadableException : Exception
{
    public ArchiveUnreadableException()
        : this(ArchiveTrouble.Broken, "The archive can't be read.")
    {
    }

    public ArchiveUnreadableException(string message)
        : this(ArchiveTrouble.Broken, message)
    {
    }

    public ArchiveUnreadableException(string message, Exception innerException)
        : this(ArchiveTrouble.Broken, message, innerException)
    {
    }

    public ArchiveUnreadableException(ArchiveTrouble trouble, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Trouble = trouble;
    }

    public ArchiveTrouble Trouble { get; }
}
