namespace ModShelf.Application.Downloads.Ports;

/// <summary>
/// What <see cref="IFileTransfer"/> reports while it works, in this order: <see cref="Started"/> once,
/// <see cref="Advanced"/> any number of times, then <see cref="Verifying"/> once the bytes are on disk.
/// </summary>
public interface ITransferProgress
{
    /// <summary>
    /// The server accepted the request. The transfer continues from <paramref name="resumeFromByte"/>
    /// (0 for a fresh start).
    /// </summary>
    void Started(long resumeFromByte, long? totalBytes);

    /// <summary>
    /// <paramref name="bytesDownloaded"/> bytes of the file are on disk.
    /// </summary>
    void Advanced(long bytesDownloaded);

    /// <summary>
    /// Every byte is on disk, and the file is being checked against the ticket's hash.
    /// </summary>
    void Verifying();
}
