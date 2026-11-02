using ModShelf.Application.Downloads;
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

    /// <param name="root">The library's root folder.</param>
    /// <param name="compact">Whether to leave out the files and updates, giving how many there are.</param>
    public static LibraryModEntry Mod(LibraryMod mod, string root, bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(mod);

        LibraryFileEntry[] files = [.. mod.Files.OrderByDescending(file => file.UploadedAt).Select(file => File(file, root))];
        return new LibraryModEntry(
            mod.Mod.Game.Source.Value,
            mod.Mod.Game.Key,
            mod.Mod.Id,
            mod.Name,
            mod.Author,
            mod.PageUrl?.AbsoluteUri,
            mod.IsWatched,
            mod.AddedAt,
            mod.CheckedAt,
            files.Length,
            files.Count(file => file.Missing),
            mod.Updates.Count,
            compact ? null : files,
            compact ? null : [.. mod.Updates.OrderByDescending(update => update.UploadedAt).Select(update => Update(update, mod, root))]);
    }

    public static LibraryFileEntry File(LibraryFile file, string root)
    {
        ArgumentNullException.ThrowIfNull(file);

        string path = LibraryPaths.Resolve(root, file.RelativePath);
        return new LibraryFileEntry(
            file.File.Id,
            file.Name,
            file.VersionLabel,
            file.Category.ToString(),
            file.FileName,
            path,
            file.SizeInBytes,
            file.UploadedAt,
            file.DownloadedAt,
            string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), ArchiveFolder, StringComparison.OrdinalIgnoreCase),
            !System.IO.File.Exists(path));
    }

    public static LibraryUpdateEntry Update(LibraryUpdate update, LibraryMod mod, string root)
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
            replaces is null ? null : File(replaces, root));
    }

    /// <param name="library">The library, for the update's mod.</param>
    public static UpdateFound Found(LibraryUpdate update, IReadOnlyDictionary<ModRef, LibraryMod> library, string root, IReadOnlyList<string>? changes = null)
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
            known is null ? Update(update, root) : Update(update, known, root),
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
    private static LibraryUpdateEntry Update(LibraryUpdate update, string root)
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
            null);
}
