using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The library and the queue as the MCP tools return them: paths absolute, enums by name.
/// </summary>
internal static class McpEntries
{
    /// <summary>
    /// The folder old versions move to, in their own folder (<c>library.oldVersions archive</c>).
    /// </summary>
    private const string ArchiveFolder = "_archive";

    /// <param name="roots">The library's root, and each game's own folder.</param>
    /// <param name="compact">Whether to leave out the files and updates, giving how many there are.</param>
    public static LibraryModEntry Mod(LibraryMod mod, LibraryRoots roots, bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(mod);

        LibraryFileEntry[] files = [.. mod.Files.OrderByDescending(file => file.UploadedAt).Select(file => File(file, roots))];
        return new LibraryModEntry(
            mod.Mod.Game.Source.Value,
            mod.Mod.Game.Key,
            mod.Mod.Id,
            mod.Name,
            mod.Author,
            mod.PageUrl?.AbsoluteUri,
            mod.IsWatched,
            mod.IsPinned,
            mod.Note,
            mod.AddedAt,
            mod.CheckedAt,
            files.Length,
            files.Count(file => file.Missing),
            mod.OfferedUpdates.Count,
            compact ? null : files,
            compact ? null : [.. mod.Updates.OrderByDescending(update => update.UploadedAt).Select(update => Update(update, mod, roots))]);
    }

    public static LibraryFileEntry File(LibraryFile file, LibraryRoots roots)
    {
        ArgumentNullException.ThrowIfNull(file);

        string path = roots.Resolve(file);
        return new LibraryFileEntry(
            file.File.Id,
            file.Name,
            file.VersionLabel,
            file.Category.ToString(),
            file.FileName,
            path,
            file.SizeInBytes,
            file.Sha256,
            file.UploadedAt,
            file.DownloadedAt,
            string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), ArchiveFolder, StringComparison.OrdinalIgnoreCase),
            !System.IO.File.Exists(path));
    }

    /// <summary>
    /// The older version <paramref name="file"/> of <paramref name="mod"/> replaced when it was downloaded, and what
    /// became of it, as <paramref name="history"/> and the library say; null when it replaced none, or the history doesn't
    /// say.
    /// </summary>
    public static ReplacedFileEntry? Replaced(LibraryMod mod, LibraryFile file, IReadOnlyList<LibraryEvent> history, LibraryRoots roots)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(history);

        // The download that brought the file as it is: a file downloaded again replaces it, at a later time.
        LibraryEvent? downloaded = history.FirstOrDefault(
            happened => happened.Kind == LibraryEventKind.Downloaded && happened.File == file.File && happened.At == file.DownloadedAt);
        if (downloaded?.Replaces is not { } replaced)
        {
            return null;
        }

        if (mod.Files.FirstOrDefault(other => other.File == replaced) is { } kept)
        {
            return new ReplacedFileEntry(
                replaced.Id, kept.VersionLabel ?? downloaded.ReplacesVersion, kept.Name, File(kept, roots).Archived ? "archived" : "kept");
        }

        bool deleted = history.Any(happened => happened.Kind == LibraryEventKind.Deleted && happened.File == replaced && happened.At >= downloaded.At);
        string? name = history.FirstOrDefault(happened => happened.File == replaced && happened.FileName is not null)?.FileName;
        return new ReplacedFileEntry(replaced.Id, downloaded.ReplacesVersion, name, deleted ? "deleted" : "gone");
    }

    public static LibraryUpdateEntry Update(LibraryUpdate update, LibraryMod mod, LibraryRoots roots)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(mod);

        LibraryFile? replaces = update.Replaces is { } replaced ? mod.Files.FirstOrDefault(file => file.File == replaced) : null;
        return new LibraryUpdateEntry(
            update.File.Id,
            update.Name,
            update.VersionLabel,
            update.Category.ToString(),
            update.Replaces is null ? "newFile" : "update",
            update.FileName,
            update.UploadedAt,
            update.FoundAt,
            update.PageUrl?.AbsoluteUri,
            replaces is null ? null : File(replaces, roots),
            mod.SkippedUpdates.Contains(update.File));
    }

    /// <param name="library">The library, for the update's mod.</param>
    public static UpdateFound Found(LibraryUpdate update, IReadOnlyDictionary<ModRef, LibraryMod> library, LibraryRoots roots, IReadOnlyList<string>? changes = null)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(library);

        ModRef mod = update.File.Mod;
        LibraryMod? known = library.GetValueOrDefault(mod);
        return new UpdateFound(
            mod.Game.Source.Value,
            mod.Game.Key,
            mod.Id,
            known?.Name ?? mod.Id,
            known is null ? Update(update, roots) : Update(update, known, roots),
            changes);
    }

    public static DownloadEntry Download(DownloadJobSnapshot job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new DownloadEntry(
            job.Id.ToString(),
            job.Status.ToString(),
            job.File.Mod.Game.Source.Value,
            job.File.Mod.Game.Key,
            job.File.Mod.Id,
            job.File.Id,
            job.FileName,
            job.TargetDirectory,
            Path.Combine(job.TargetDirectory, job.FileName),
            job.BytesDownloaded,
            job.TotalBytes,
            job.FailureReason?.ToString(),
            job.CreatedAt,
            job.HandoffPage?.AbsoluteUri);
    }

    /// <summary>
    /// An update of a mod the library doesn't have, such as one a check found that was unwatched meanwhile.
    /// </summary>
    private static LibraryUpdateEntry Update(LibraryUpdate update, LibraryRoots roots)
        => new(
            update.File.Id,
            update.Name,
            update.VersionLabel,
            update.Category.ToString(),
            update.Replaces is null ? "newFile" : "update",
            update.FileName,
            update.UploadedAt,
            update.FoundAt,
            update.PageUrl?.AbsoluteUri,
            null,
            false);
}
