using System.Net;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Library;

/// <summary>
/// Rescans mods' folders against their sites' lists of files, and reads the hashes the library lacks (ADR 0045). Each
/// mod changes in one <see cref="ILibraryRepository.UpdateAsync"/>, published to the library's changes.
/// </summary>
internal sealed partial class LibraryRescanner : ILibraryRescan
{
    // A site may give a file's size rounded to the kilobyte, as Nexus Mods does for some files.
    private const long SizeTolerance = 1024;

    private readonly ISourceRegistry _sources;
    private readonly ILibraryRepository _repository;
    private readonly ISettingsService _settings;
    private readonly ILibraryStorage _storage;
    private readonly LibraryChanges _changes;
    private readonly LibraryHistory _history;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LibraryRescanner> _logger;

    public LibraryRescanner(
        ISourceRegistry sources,
        ILibraryRepository repository,
        ISettingsService settings,
        ILibraryStorage storage,
        LibraryChanges changes,
        LibraryHistory history,
        TimeProvider timeProvider,
        ILogger<LibraryRescanner> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _repository = repository;
        _settings = settings;
        _storage = storage;
        _changes = changes;
        _history = history;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<LibraryRescan> RescanAsync(Uri? link, CancellationToken cancellationToken)
    {
        AppSettings settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        string root = await _settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LibraryMod> library = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);

        // A library mod is found by its page there, so one its site removed can still be rescanned.
        ModRef[] mods = link is null
            ? [.. library.Select(mod => mod.Mod)]
            : [LibraryLinks.ModAt(library, link) ?? (await _sources.RecognizeLinkAsync(link, cancellationToken).ConfigureAwait(false)).File.Mod];

        Scan scan = new(root, settings, library);
        foreach (ModRef mod in mods)
        {
            await RescanModAsync(scan, mod, library.FirstOrDefault(stored => stored.Mod == mod), cancellationToken).ConfigureAwait(false);
        }

        // A file a mod sharing a folder didn't match may be another's, matched later.
        LibraryRescan rescan = new(mods.Length, scan.Added, scan.Hashed, scan.Missing, [.. scan.Unmatched.Where(path => !scan.Known.Contains(path))], scan.Failures);
        LogRescanned(_logger, mods.Length, rescan.Added.Count, rescan.Hashed.Count, rescan.Missing.Count, rescan.Unmatched.Count, rescan.Failures.Count);
        return rescan;
    }

    private static bool StopsTheSource(Exception exception)
        => exception is AuthenticationFailedException
            or HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
            or HttpRequestException { StatusCode: null };

    /// <summary>
    /// Whether a file on disk of <paramref name="size"/> bytes, named <paramref name="name"/>, is the site's file, saved as
    /// a download saves it: under its own name, made safe, or with its ID added.
    /// </summary>
    private static bool Matches(string name, long? size, ModFile file)
    {
        string saved = LibraryNames.File(file.FileName);
        bool named = string.Equals(name, saved, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, LibraryLocations.WithId(saved, file.Reference.Id), StringComparison.OrdinalIgnoreCase);
        return named && (file.SizeInBytes is not { } listed || (size is { } found && Math.Abs(found - listed) <= SizeTolerance));
    }

    private async Task RescanModAsync(Scan scan, ModRef mod, LibraryMod? stored, CancellationToken cancellationToken)
    {
        // What needs no site: the hashes the library lacks, and the files that are gone.
        List<LibraryFile> hashed = [];
        foreach (LibraryFile file in stored?.Files ?? [])
        {
            string path = LibraryPaths.Resolve(scan.Root, file.RelativePath);
            if (!_storage.FileExists(path))
            {
                scan.Missing.Add(file);
            }
            else if (file.Sha256 is null && await HashAsync(path, cancellationToken).ConfigureAwait(false) is { } sha256)
            {
                hashed.Add(new LibraryFile(
                    file.File, file.Name, file.VersionLabel, file.Category, file.UploadedAt, file.RelativePath, file.SizeInBytes, file.DownloadedAt, sha256));
            }
        }

        // A mod not in the library is looked for where its game's pattern puts it, which needs its name from its site.
        IModCatalog? catalog = _sources.Find<IModCatalog>(mod.Game.Source);
        ModInfo? info = null;
        if (stored is null)
        {
            info = await AskAsync(scan, mod, catalog, site => site.GetModAsync(mod, cancellationToken)).ConfigureAwait(false);
            if (info is null)
            {
                return;
            }
        }

        string name = stored?.Name ?? info!.Name;
        string[] unknown = [.. FoldersOf(scan, mod, stored, name).SelectMany(ListFiles).Where(path => !scan.Known.Contains(path)).Distinct(StringComparer.OrdinalIgnoreCase)];
        List<LibraryFile> added = [];
        if (unknown.Length > 0
            && await AskAsync(scan, mod, catalog, site => site.GetFilesAsync(mod, cancellationToken)).ConfigureAwait(false) is { } siteFiles)
        {
            HashSet<ModFileRef> have = [.. stored?.Files.Select(file => file.File) ?? []];
            DateTimeOffset now = _timeProvider.GetUtcNow();
            foreach (string path in unknown.Order(StringComparer.Ordinal))
            {
                long? size = _storage.FileSize(path);
                ModFile[] candidates =
                [
                    .. siteFiles.Where(file => !have.Contains(file.Reference) && Matches(Path.GetFileName(path), size, file)),
                ];
                if (candidates is not [ModFile match] || size is not { } length || LibraryPaths.RelativeTo(scan.Root, path) is not { } relativePath)
                {
                    scan.Unmatched.Add(path);
                    continue;
                }

                string? sha256 = await HashAsync(path, cancellationToken).ConfigureAwait(false);
                added.Add(new LibraryFile(
                    match.Reference, match.Name, match.VersionLabel, match.Category, match.UploadedAt, relativePath, length, now, sha256));
                have.Add(match.Reference);
                scan.Known.Add(path);
            }
        }

        if (hashed.Count == 0 && added.Count == 0)
        {
            return;
        }

        LibraryChange change = await _repository.UpdateAsync(
                mod,
                current =>
                {
                    if (current is null)
                    {
                        return added.Count == 0 || info is null
                            ? null
                            : added.Aggregate(LibraryMod.Watch(mod, info.Name, info.Author, info.PageUrl, _timeProvider.GetUtcNow()), (changed, file) => changed.WithFile(file));
                    }

                    // Only what's as it was read: a file changed meanwhile, by a download or a move, is left to that.
                    LibraryMod result = current;
                    foreach (LibraryFile file in hashed)
                    {
                        if (result.Files.FirstOrDefault(kept => kept.File == file.File) is { Sha256: null } kept && kept.RelativePath == file.RelativePath)
                        {
                            result = result.WithFile(file);
                        }
                    }

                    foreach (LibraryFile file in added.Where(file => result.Files.All(kept => kept.File != file.File)))
                    {
                        result = result.WithFile(file);
                    }

                    return result;
                },
                cancellationToken)
            .ConfigureAwait(false);
        _changes.Publish(change);

        IReadOnlyList<LibraryFile> after = change.After?.Files ?? [];
        scan.Hashed.AddRange(hashed.Where(file => after.Any(kept => kept.File == file.File && kept.Sha256 == file.Sha256)));
        List<LibraryEvent> found = [];
        foreach (LibraryFile file in added.Where(file => after.Any(kept => kept.File == file.File && kept.RelativePath == file.RelativePath)))
        {
            scan.Added.Add(file);
            LogAdded(_logger, file.FileName, mod.Id, mod.Game.Key);
            found.Add(LibraryEvent.Of(file.DownloadedAt, LibraryEventKind.Found, change.After!, file));
        }

        await _history.RecordAsync(found, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// The library's folders of the mod, and where its game's pattern puts its new files, each with its <c>_archive</c>.
    /// </summary>
    private static IEnumerable<string> FoldersOf(Scan scan, ModRef mod, LibraryMod? stored, string name)
    {
        string byPattern = LibraryLocations.FolderOf(
            scan.Settings.FoldersFor(mod.Game).Render(mod.Game.Source.Value, mod.Game.Key, name, mod.Id, "file"));
        return (stored?.Files.Select(file => LibraryLocations.ModFolderOf(file.RelativePath)) ?? [])
            .Append(byPattern)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(folder => new[] { folder, folder.Length == 0 ? OldVersions.ArchiveFolder : $"{folder}{LibraryPaths.Separator}{OldVersions.ArchiveFolder}" })
            .Select(folder => folder.Length == 0 ? scan.Root : LibraryPaths.Resolve(scan.Root, folder));
    }

    /// <summary>
    /// The files in a folder; none when it can't be listed, which is logged.
    /// </summary>
    private IReadOnlyList<string> ListFiles(string folder)
    {
        try
        {
            return _storage.FilesIn(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantList(_logger, exception, folder);
            return [];
        }
    }

    /// <summary>
    /// Asks the mod's site, once its source isn't stopped; a problem is the mod's failure, and a refused account or a
    /// request to slow down stops asking that source for the rest of the rescan.
    /// </summary>
    /// <returns>The site's answer; null when it couldn't be had.</returns>
    private async Task<T?> AskAsync<T>(Scan scan, ModRef mod, IModCatalog? catalog, Func<IModCatalog, Task<T>> ask)
        where T : class
    {
        if (catalog is null)
        {
            scan.Failures.Add(new RescanFailure(mod, $"{mod.Game.Source} can't list its mods' files, so they can't be matched."));
            return null;
        }

        if (scan.Stopped.TryGetValue(mod.Game.Source, out string? why))
        {
            scan.Failures.Add(new RescanFailure(mod, $"Not asked: {mod.Game.Source} refused an earlier question in this rescan. {why}"));
            return null;
        }

        try
        {
            return await ask(catalog).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCantAsk(_logger, exception, mod.Id, mod.Game.Key);
            scan.Failures.Add(new RescanFailure(mod, exception.Message));
            if (StopsTheSource(exception))
            {
                scan.Stopped[mod.Game.Source] = exception.Message;
            }

            return null;
        }
    }

    /// <summary>
    /// The SHA-256 of the file at <paramref name="path"/>; null when it couldn't be read, which is logged.
    /// </summary>
    private async Task<string?> HashAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _storage.Sha256Async(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantHash(_logger, exception, Path.GetFileName(path));
            return null;
        }
    }

    /// <summary>
    /// A rescan under way: what it knows, and what it found so far.
    /// </summary>
    private sealed class Scan(string root, AppSettings settings, IReadOnlyList<LibraryMod> library)
    {
        public string Root { get; } = root;

        public AppSettings Settings { get; } = settings;

        /// <summary>
        /// The paths of the library's files, and of those added so far: in any case, since case doesn't matter on
        /// Windows and macOS.
        /// </summary>
        public HashSet<string> Known { get; } =
            new(library.SelectMany(mod => mod.Files).Select(file => LibraryPaths.Resolve(root, file.RelativePath)), StringComparer.OrdinalIgnoreCase);

        public List<LibraryFile> Added { get; } = [];

        public List<LibraryFile> Hashed { get; } = [];

        public List<LibraryFile> Missing { get; } = [];

        // A folder several mods share is listed for each, so its other files would come up once per mod.
        public SortedSet<string> Unmatched { get; } = new(StringComparer.Ordinal);

        public List<RescanFailure> Failures { get; } = [];

        /// <summary>
        /// The sources not to ask again, and why.
        /// </summary>
        public Dictionary<SourceId, string> Stopped { get; } = [];
    }
}
