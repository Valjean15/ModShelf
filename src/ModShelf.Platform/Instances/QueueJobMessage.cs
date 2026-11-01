namespace ModShelf.Platform.Instances;

/// <summary>
/// A download, as a queue reply carries it.
/// </summary>
internal sealed record QueueJobMessage(
    string Id,
    string Source,
    string Game,
    string Mod,
    string File,
    string TargetDirectory,
    string FileName,
    string Status,
    long BytesDownloaded,
    long? TotalBytes,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    string? HandoffPage);
