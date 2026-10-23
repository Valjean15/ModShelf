using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// A row of the <c>DownloadJobs</c> table. The repository maps it to and from <see cref="DownloadJob"/>,
/// so the domain types never have to be shaped for EF Core.
/// </summary>
/// <remarks>
/// A mutable class rather than a record: EF Core tracks entities by reference, which value equality would confuse.
/// </remarks>
internal sealed class DownloadJobRecord
{
    /// <summary>
    /// The row's key, assigned by SQLite in insertion order. It gives jobs a stable oldest-first order.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The job's <see cref="DownloadJobId"/>.
    /// </summary>
    public Guid Id { get; set; }

    public required string SourceId { get; set; }

    public required string GameKey { get; set; }

    public required string ModId { get; set; }

    public required string FileId { get; set; }

    public required string TargetDirectory { get; set; }

    public required string FileName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DownloadJobStatus Status { get; set; }

    public long BytesDownloaded { get; set; }

    public long? TotalBytes { get; set; }

    public DownloadFailureReason? FailureReason { get; set; }

    public static DownloadJobRecord From(DownloadJob job) => new()
    {
        Id = job.Id.Value,
        SourceId = job.File.Mod.Game.Source.Value,
        GameKey = job.File.Mod.Game.Key,
        ModId = job.File.Mod.Id,
        FileId = job.File.Id,
        TargetDirectory = job.TargetDirectory,
        FileName = job.FileName,
        CreatedAt = job.CreatedAt,
        Status = job.Status,
        BytesDownloaded = job.BytesDownloaded,
        TotalBytes = job.TotalBytes,
        FailureReason = job.FailureReason,
    };

    /// <summary>
    /// Rebuilds the job this row stores.
    /// </summary>
    /// <exception cref="InvalidDataException">The row holds values no job can have.</exception>
    public DownloadJob ToJob()
    {
        try
        {
            ModFileRef file = new(new ModRef(new GameRef(new SourceId(SourceId), GameKey), ModId), FileId);
            return DownloadJob.Restore(
                new DownloadJobId(Id),
                file,
                TargetDirectory,
                FileName,
                CreatedAt,
                Status,
                BytesDownloaded,
                TotalBytes,
                FailureReason);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"Download job {Id} in the library database can't be loaded: {exception.Message}", exception);
        }
    }
}
