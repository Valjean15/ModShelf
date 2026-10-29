namespace ModShelf.Infrastructure.Downloads;

/// <summary>
/// What the server said of the file whose start a partial file holds, kept beside it so that a later attempt continues
/// it only while the server's file is still that one.
/// </summary>
/// <param name="Length">The whole file's length in bytes, when the server said.</param>
/// <param name="ETag">The file's strong entity tag, quoted, when the server gave one.</param>
/// <param name="LastModified">When the server said the file last changed.</param>
internal sealed record PartialFileRecord(long? Length, string? ETag, DateTimeOffset? LastModified);
