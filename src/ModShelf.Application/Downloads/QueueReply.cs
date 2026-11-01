namespace ModShelf.Application.Downloads;

/// <summary>
/// What became of a <see cref="QueueRequest"/>.
/// </summary>
/// <param name="Outcome">How it went.</param>
/// <param name="Problem">Unless it's done, why, in words for the user.</param>
/// <param name="Job">The download, as the request left it; null when there's none, as for a file the library has.</param>
/// <param name="LibraryPath">
/// For a download, where the file is, or goes, in the library: an absolute path; null when a download outside the
/// library went on with the link.
/// </param>
/// <param name="AlreadyQueued">For a download, whether an unfinished download of the file went on, rather than a new one.</param>
/// <param name="AlreadySaved">For a download, whether the file was there already, so nothing downloads.</param>
public sealed record QueueReply(
    QueueOutcome Outcome,
    string? Problem = null,
    DownloadJobSnapshot? Job = null,
    string? LibraryPath = null,
    bool AlreadyQueued = false,
    bool AlreadySaved = false);
