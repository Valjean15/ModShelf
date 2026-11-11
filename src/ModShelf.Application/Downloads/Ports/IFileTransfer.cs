using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Application.Downloads.Ports;

/// <summary>
/// Moves a file's bytes from a download ticket to disk. Implemented by Infrastructure.
/// </summary>
public interface IFileTransfer
{
    /// <summary>
    /// Downloads the ticket's URL to <paramref name="destinationPath"/>, resuming from the partial file an
    /// earlier attempt left behind. The file appears at <paramref name="destinationPath"/> only once it is
    /// complete and, when the ticket has an expected hash, verified.
    /// </summary>
    /// <remarks>
    /// Expected problems come back as a <see cref="TransferOutcome"/>. Cancellation throws
    /// <see cref="OperationCanceledException"/> and keeps the partial file, so the download can resume.
    /// </remarks>
    Task<TransferOutcome> DownloadAsync(
        DownloadTicket ticket,
        string destinationPath,
        ITransferProgress progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes a file downloaded elsewhere, such as by the browser: checks it against <paramref name="expectedHash"/>, when
    /// there is one, then moves it to <paramref name="destinationPath"/>, renamed on the same drive, or copied and then
    /// removed from another. The file appears at <paramref name="destinationPath"/> only once it's whole.
    /// </summary>
    /// <returns>
    /// <see cref="TransferOutcome.Completed"/>; <see cref="TransferOutcome.HashMismatch"/> or
    /// <see cref="TransferOutcome.StorageError"/> with the file left where it was.
    /// </returns>
    /// <exception cref="FileNotFoundException">There's no file at <paramref name="sourcePath"/>.</exception>
    Task<TransferOutcome> TakeAsync(string sourcePath, FileHash? expectedHash, string destinationPath, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the partial file of an abandoned download, if there is one. Never touches
    /// <paramref name="destinationPath"/> itself.
    /// </summary>
    Task DeletePartialFileAsync(string destinationPath, CancellationToken cancellationToken);
}
