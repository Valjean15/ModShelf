using System.Net;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Lookup;

namespace ModShelf.Application.Archives;

/// <summary>
/// Imports a folder's archives into the library, copying only (ADR 0101). Identifying them reads each once and asks
/// the site as little as it can; importing asks nothing, and holds the library (<see cref="ILibraryHold"/>) one archive
/// at a time, as a download being recorded does.
/// </summary>
internal sealed partial class ArchiveImport : IArchiveImport
{
    private readonly ISourceRegistry _sources;
    private readonly ILibraryRepository _repository;
    private readonly ISettingsService _settings;
    private readonly ILibraryStorage _storage;
    private readonly ILibraryHold _hold;
    private readonly LibraryChanges _changes;
    private readonly LibraryHistory _history;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ArchiveImport> _logger;

    public ArchiveImport(
        ISourceRegistry sources,
        ILibraryRepository repository,
        ISettingsService settings,
        ILibraryStorage storage,
        ILibraryHold hold,
        LibraryChanges changes,
        LibraryHistory history,
        TimeProvider timeProvider,
        ILogger<ArchiveImport> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _repository = repository;
        _settings = settings;
        _storage = storage;
        _hold = hold;
        _changes = changes;
        _history = history;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ArchiveScan> ScanAsync(string folder, GameRef game, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(game);

        string site = SiteName(game.Source);
        if (_sources.Find<IModCatalog>(game.Source) is null)
        {
            throw new ArgumentException($"{site} can't list its mods' files, so archives of its games can't be identified.", nameof(game));
        }

        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        AppSettings settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        LibraryRoots roots = LibraryRoots.Of(settings, _settings.DefaultLibraryRoot);
        if (IsInLibrary(roots, game, full))
        {
            throw new ArgumentException(
                $"{full} is in the library, whose files can't be imported into it. To add files placed there by hand, rescan the library.",
                nameof(folder));
        }

        IReadOnlyList<string> files = _storage.FilesIn(full);
        HashSet<string> listed = new(files, StringComparer.OrdinalIgnoreCase);
        Dictionary<ModRef, LibraryMod> stored = (await _repository.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(mod => mod.Mod);
        List<ScannedArchive> archives = [];
        foreach (string path in files.Where(ArchiveNames.IsArchive))
        {
            if (_storage.FileSize(path) is not { } size)
            {
                continue;
            }

            ModOrganizerNote? note = listed.Contains(path + ModOrganizerNotes.Extension)
                ? await ReadNoteAsync(path + ModOrganizerNotes.Extension, cancellationToken).ConfigureAwait(false)
                : null;
            archives.Add(note is not null
                ? new ScannedArchive(path, size, ArchiveClue.Notes, note.ModId, note.FileId)
                : ArchiveNames.ModIdOf(path) is { } modId
                    ? new ScannedArchive(path, size, ArchiveClue.Name, modId, null)
                    : new ScannedArchive(path, size, null, null, null));
        }

        bool canLookUp = LookupOf(game) is not null;
        int requests = archives.Where(archive => archive.ModId is not null).Select(archive => new ModRef(game, archive.ModId!)).Distinct()
                .Sum(mod => stored.ContainsKey(mod) ? 1 : 2)
            + (canLookUp ? archives.Count(archive => archive.ModId is null) : 0);
        int named = archives.Count(archive => archive.Clue is not null);
        LogScanned(_logger, archives.Count, named, game.Source.Value, game.Key);
        return new ArchiveScan(full, game, archives, files.Count - archives.Count, requests, canLookUp);
    }

    public async Task<ArchivePlan> IdentifyAsync(ArchiveScan scan, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);

        IReadOnlyList<LibraryMod> library = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        Identifying identifying = new(scan, library, _sources.Find<IModCatalog>(scan.Game.Source), LookupOf(scan.Game));
        List<PlannedArchive> planned = [];
        for (int index = 0; index < scan.Archives.Count; index++)
        {
            planned.Add(await IdentifyAsync(identifying, scan.Archives[index], cancellationToken).ConfigureAwait(false));
            progress?.Report(index + 1);
        }

        ArchivePlan plan = new(scan.Folder, scan.Game, planned);
        int identified = plan.Count(ArchiveState.Identified);
        int inLibrary = plan.Count(ArchiveState.InLibrary);
        int duplicates = plan.Count(ArchiveState.Duplicate);
        int unknown = plan.Count(ArchiveState.Unknown);
        LogIdentified(_logger, planned.Count, identified, inLibrary, duplicates, unknown, identifying.Requests);
        return plan;
    }

    public async Task<ArchiveImportResult> ImportAsync(ArchivePlan plan, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        AppSettings settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        LibraryRoots roots = LibraryRoots.Of(settings, _settings.DefaultLibraryRoot);
        IReadOnlyList<PlannedArchive> toImport = plan.ToImport;
        List<ImportedArchive> imported = [];
        List<ArchiveProblem> problems = [];
        int modsAdded = 0;
        int inLibrary = 0;
        for (int index = 0; index < toImport.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlannedArchive archive = toImport[index];
            using (await _hold.HoldAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (await ImportAsync(archive, plan.Game, settings, roots, cancellationToken).ConfigureAwait(false))
                {
                    case Imported done:
                        imported.Add(done.Archive);
                        modsAdded += done.NewMod ? 1 : 0;
                        break;
                    case AlreadyIn:
                        inLibrary++;
                        break;
                    case Failed failed:
                        problems.Add(new ArchiveProblem(archive.Path, failed.Message));
                        break;
                }
            }

            progress?.Report(index + 1);
        }

        LogImported(_logger, imported.Count, modsAdded, inLibrary, problems.Count, plan.Game.Source.Value, plan.Game.Key);
        return new ArchiveImportResult(imported, modsAdded, inLibrary, problems);
    }

    private static bool StopsTheSource(Exception exception)
        => exception is AuthenticationFailedException
            or HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
            or HttpRequestException { StatusCode: null };

    /// <summary>
    /// Whether a folder is one of the library's, or in one.
    /// </summary>
    private static bool IsInLibrary(LibraryRoots roots, GameRef game, string folder)
    {
        string inside = Path.Combine(folder, "file");
        return new[] { roots.Root, roots.For(game) }.Concat(roots.Games.Values)
            .Any(root => LibraryPaths.RelativeTo(root, inside) is not null);
    }

    /// <summary>
    /// The one file of several that the archive is: the one named as it is, or the one of a mod the library has.
    /// </summary>
    private static FoundFile? OneOf(IReadOnlyList<FoundFile> found, ScannedArchive archive, Dictionary<ModRef, LibraryMod> stored)
    {
        if (found is [FoundFile only])
        {
            return only;
        }

        FoundFile[] named = [.. found.Where(file => SiteFiles.Matches(archive.Name, archive.SizeInBytes, file.File))];
        if (named is [FoundFile byName])
        {
            return byName;
        }

        FoundFile[] kept = [.. (named.Length > 0 ? named : found).Where(file => stored.ContainsKey(file.File.Reference.Mod))];
        return kept is [FoundFile ofLibrary] ? ofLibrary : null;
    }

    private async Task<PlannedArchive> IdentifyAsync(Identifying identifying, ScannedArchive archive, CancellationToken cancellationToken)
    {
        FileHashes hashes;
        try
        {
            hashes = await _storage.HashesAsync(archive.Path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantRead(_logger, exception, archive.Name);
            return new PlannedArchive(archive.Path, archive.SizeInBytes, ArchiveState.Unknown, Why: $"It couldn't be read: {exception.Message}");
        }

        if (identifying.Shas.TryGetValue(hashes.Sha256, out (LibraryMod Mod, LibraryFile File) kept))
        {
            return new PlannedArchive(
                archive.Path, archive.SizeInBytes, ArchiveState.InLibrary, File: kept.File.File, ModName: kept.Mod.Name, FileName: kept.File.Name, Sha256: hashes.Sha256);
        }

        if (identifying.Seen.TryGetValue(hashes.Sha256, out string? first))
        {
            return new PlannedArchive(
                archive.Path, archive.SizeInBytes, ArchiveState.Duplicate, Why: $"It's the same as {Path.GetFileName(first)}.", Sha256: hashes.Sha256);
        }

        identifying.Seen[hashes.Sha256] = archive.Path;
        (ModFile File, ModInfo? Mod, ArchiveClue By)? match = null;
        if (archive is { Clue: { } clue, ModId: { } modId }
            && await FilesOfAsync(identifying, new ModRef(identifying.Game, modId), cancellationToken).ConfigureAwait(false) is { } siteFiles)
        {
            ModFile[] candidates = archive.FileId is { } fileId
                ? [.. siteFiles.Where(file => file.Reference.Id == fileId && SiteFiles.SizeFits(archive.SizeInBytes, file))]
                : [.. siteFiles.Where(file => SiteFiles.Matches(archive.Name, archive.SizeInBytes, file))];
            if (candidates is [ModFile confirmed])
            {
                match = (confirmed, null, clue);
            }
        }

        string? why = null;
        if (match is null)
        {
            (FoundFile? found, why) = await LookUpAsync(identifying, archive, hashes, cancellationToken).ConfigureAwait(false);
            if (found is not null)
            {
                match = (found.File, found.Mod, ArchiveClue.Hash);
            }
        }

        if (match is not { } identified)
        {
            return new PlannedArchive(archive.Path, archive.SizeInBytes, ArchiveState.Unknown, Why: why, Sha256: hashes.Sha256);
        }

        ModFileRef file = identified.File.Reference;
        LibraryMod? stored = identifying.Stored.GetValueOrDefault(file.Mod);
        if (stored?.Files.FirstOrDefault(kept => kept.File == file) is { } same)
        {
            return new PlannedArchive(
                archive.Path, archive.SizeInBytes, ArchiveState.InLibrary, identified.By, file, stored.Name, same.Name, Sha256: hashes.Sha256);
        }

        // Another archive here may be the same file with other content, such as one changed since.
        if (!identifying.Planned.Add(file))
        {
            return new PlannedArchive(
                archive.Path, archive.SizeInBytes, ArchiveState.Duplicate, identified.By, file, FileName: identified.File.Name,
                Why: "Another archive here is the same file of its site.", Sha256: hashes.Sha256);
        }

        if (stored is not null)
        {
            return new PlannedArchive(
                archive.Path, archive.SizeInBytes, ArchiveState.Identified, identified.By, file, stored.Name, identified.File.Name, Sha256: hashes.Sha256)
            {
                SiteFile = identified.File,
            };
        }

        ModInfo? mod = identified.Mod ?? await ModOfAsync(identifying, file.Mod, cancellationToken).ConfigureAwait(false);
        return mod is null
            ? new PlannedArchive(
                archive.Path, archive.SizeInBytes, ArchiveState.Unknown, identified.By, file, FileName: identified.File.Name,
                Why: identifying.Why ?? $"{identifying.Site} didn't describe its mod.", Sha256: hashes.Sha256)
            : new PlannedArchive(
                archive.Path, archive.SizeInBytes, ArchiveState.Identified, identified.By, file, mod.Name, identified.File.Name, Sha256: hashes.Sha256)
            {
                Mod = mod,
                SiteFile = identified.File,
            };
    }

    /// <summary>
    /// Looks the archive up by its hash on its site, when it can.
    /// </summary>
    /// <returns>The file it is; or null and why not.</returns>
    private async Task<(FoundFile? Found, string Why)> LookUpAsync(
        Identifying identifying,
        ScannedArchive archive,
        FileHashes hashes,
        CancellationToken cancellationToken)
    {
        if (identifying.Lookup is not { } lookup)
        {
            return (null, archive.Clue switch
            {
                ArchiveClue.Notes => $"Its Mod Organizer note names mod {archive.ModId}, but none of that mod's files on {identifying.Site} matches it.",
                ArchiveClue.Name => $"Its name gives mod {archive.ModId}, but none of that mod's files on {identifying.Site} matches it.",
                _ => $"{identifying.Site} can't find archives by their content, and neither a note nor its name says which mod it's of.",
            });
        }

        if (identifying.Why is { } stopped)
        {
            return (null, stopped);
        }

        FileHash hash = new(lookup.LookupAlgorithm, lookup.LookupAlgorithm == FileHashAlgorithm.Md5 ? hashes.Md5 : hashes.Sha256);
        IReadOnlyList<FoundFile> found;
        try
        {
            identifying.Requests++;
            found = await lookup.FindByHashAsync(identifying.Game, hash, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return (null, Refused(identifying, exception, $"{identifying.Site} couldn't look it up"));
        }

        return found.Count == 0
            ? (null, $"{identifying.Site} has no file of the game with its content: it may be from elsewhere, or changed since.")
            : OneOf(found, archive, identifying.Stored) is { } one
                ? (one, string.Empty)
                : (null, $"{identifying.Site} has {found.Count} files of the game with its content, of several mods.");
    }

    /// <summary>
    /// The mod's files on its site, asked once a mod; null when they can't be had.
    /// </summary>
    private async Task<IReadOnlyList<ModFile>?> FilesOfAsync(Identifying identifying, ModRef mod, CancellationToken cancellationToken)
    {
        if (identifying.Files.TryGetValue(mod, out IReadOnlyList<ModFile>? known))
        {
            return known;
        }

        if (identifying.Catalog is not { } catalog || identifying.Why is not null)
        {
            return null;
        }

        IReadOnlyList<ModFile>? files = null;
        try
        {
            identifying.Requests++;
            files = await catalog.GetFilesAsync(mod, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A mod a note or a name misleads to, such as one the site doesn't have, is no reason to stop.
            _ = Refused(identifying, exception, $"{identifying.Site} couldn't list mod {mod.Id}'s files");
        }

        identifying.Files[mod] = files;
        return files;
    }

    /// <summary>
    /// The mod as its site describes it, asked once a mod; null when it can't be had.
    /// </summary>
    private async Task<ModInfo?> ModOfAsync(Identifying identifying, ModRef mod, CancellationToken cancellationToken)
    {
        if (identifying.Mods.TryGetValue(mod, out ModInfo? known))
        {
            return known;
        }

        ModInfo? info = null;
        if (identifying.Catalog is { } catalog && identifying.Why is null)
        {
            try
            {
                identifying.Requests++;
                info = await catalog.GetModAsync(mod, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _ = Refused(identifying, exception, $"{identifying.Site} couldn't describe mod {mod.Id}");
            }
        }

        identifying.Mods[mod] = info;
        return info;
    }

    /// <summary>
    /// Logs a refusal, and stops asking the site when it's one that stops it.
    /// </summary>
    /// <returns>What to say of the archive.</returns>
    private string Refused(Identifying identifying, Exception exception, string what)
    {
        LogCantAsk(_logger, exception, identifying.Game.Source.Value, identifying.Game.Key);
        string said = $"{what}: {exception.Message}";
        if (StopsTheSource(exception))
        {
            identifying.Why = $"Not looked up: {identifying.Site} refused an earlier request. {exception.Message}";
        }

        return said;
    }

    private async Task<Outcome> ImportAsync(
        PlannedArchive archive,
        GameRef game,
        AppSettings settings,
        LibraryRoots roots,
        CancellationToken cancellationToken)
    {
        if (archive is not { File: { } file, SiteFile: { } siteFile, Sha256: { } sha256 })
        {
            return new Failed("It wasn't identified here.");
        }

        IReadOnlyList<LibraryMod> library = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        LibraryMod? stored = library.FirstOrDefault(mod => mod.Mod == file.Mod);
        if (stored?.Files.Any(kept => kept.File == file) == true
            || library.Any(mod => mod.Files.Any(kept => string.Equals(kept.Sha256, sha256, StringComparison.OrdinalIgnoreCase))))
        {
            return new AlreadyIn();
        }

        if (stored is null && archive.Mod is null)
        {
            return new Failed("Its mod left the library since it was identified; identify the archives again.");
        }

        string modName = stored?.Name ?? archive.Mod!.Name;
        string pathByPattern = settings.FoldersFor(game).Render(game.Source.Value, game.Key, modName, file.Mod.Id, siteFile.FileName);
        string folder = stored is { Files.Count: > 0 }
            ? LibraryLocations.ModFolderOf(stored.Files[^1].RelativePath)
            : LibraryLocations.FolderOf(pathByPattern);
        string root = roots.For(game);
        if (FreePath(root, library, folder, LibraryLocations.NameOf(pathByPattern), file) is not { } relativePath)
        {
            return new Failed($"Its folder has files of both its names already: {LibraryLocations.NameOf(pathByPattern)}, and with its ID added.");
        }

        string target = LibraryPaths.Resolve(root, relativePath);
        try
        {
            _storage.EnsurePathFits(target);
            if (_storage.FreeSpace(Path.GetDirectoryName(target)!) is { } free && free < archive.SizeInBytes)
            {
                return new Failed("There isn't room for it on the library's drive.");
            }

            await _storage.CopyFileAsync(archive.Path, target, sha256, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogCantCopy(_logger, exception, archive.Name);
            return new Failed($"It couldn't be copied: {exception.Message}");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        LibraryFile recorded = new(
            file, siteFile.Name, siteFile.VersionLabel, siteFile.Category, siteFile.UploadedAt, relativePath, archive.SizeInBytes, now, sha256);
        bool isNew = false;
        LibraryChange change = await _repository.UpdateAsync(
                file.Mod,
                current =>
                {
                    // Meanwhile, another process may have brought the file in.
                    if (current?.Files.Any(kept => kept.File == file) == true)
                    {
                        return current;
                    }

                    isNew = current is null;
                    LibraryMod mod = current ?? LibraryMod.Watch(file.Mod, archive.Mod!.Name, archive.Mod.Author, archive.Mod.PageUrl, now);
                    return mod.WithFile(recorded);
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        _changes.Publish(change);
        if (change.After?.Files.FirstOrDefault(kept => kept.File == file) is not { } kept || kept.RelativePath != relativePath)
        {
            // The copy is ModShelf's own, made a moment ago: the original stays where it was.
            try
            {
                _storage.DeleteFile(target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogCantCopy(_logger, exception, archive.Name);
            }

            return new AlreadyIn();
        }

        LogCopied(_logger, archive.Name, file.Mod.Id, game.Key);
        await _history.RecordAsync([LibraryEvent.Of(now, LibraryEventKind.Found, change.After!, kept)], CancellationToken.None).ConfigureAwait(false);
        return new Imported(new ImportedArchive(archive.Path, file.Mod, kept), isNew);
    }

    /// <summary>
    /// The file's path in <paramref name="folder"/>: under its own name, or with its ID added when that's taken, by
    /// another file of the library or one on disk, in any case; null when both are.
    /// </summary>
    private string? FreePath(string root, IReadOnlyList<LibraryMod> library, string folder, string name, ModFileRef file)
    {
        foreach (string candidate in new[] { name, LibraryLocations.WithId(name, file.Id) })
        {
            string relativePath = folder.Length == 0 ? candidate : $"{folder}{LibraryPaths.Separator}{candidate}";
            bool taken = library.SelectMany(mod => mod.Files).Any(other => string.Equals(other.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                || _storage.FileExists(LibraryPaths.Resolve(root, relativePath));
            if (!taken)
            {
                return relativePath;
            }
        }

        return null;
    }

    private IFileLookup? LookupOf(GameRef game)
        => _sources.Find<IFileLookup>(game.Source) is { LookupAlgorithm: FileHashAlgorithm.Md5 or FileHashAlgorithm.Sha256 } lookup ? lookup : null;

    /// <summary>
    /// What Mod Organizer 2 noted of an archive; null when there's nothing to read, which a note that can't be read is
    /// too, as it's logged.
    /// </summary>
    private async Task<ModOrganizerNote?> ReadNoteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return ModOrganizerNotes.Read(await _storage.ReadSmallFileAsync(path, ModOrganizerNotes.MaxBytes, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantReadNote(_logger, exception, Path.GetFileName(path));
            return null;
        }
    }

    private string SiteName(SourceId source) => _sources.Find<IModSource>(source)?.DisplayName ?? source.Value;

    /// <summary>
    /// Identifying under way: what the library has, what the site said, and whether it's still asked.
    /// </summary>
    private sealed class Identifying(ArchiveScan scan, IReadOnlyList<LibraryMod> library, IModCatalog? catalog, IFileLookup? lookup)
    {
        public GameRef Game { get; } = scan.Game;

        public string Site { get; } = catalog?.DisplayName ?? lookup?.DisplayName ?? scan.Game.Source.Value;

        public IModCatalog? Catalog { get; } = catalog;

        public IFileLookup? Lookup { get; } = lookup;

        public Dictionary<ModRef, LibraryMod> Stored { get; } = library.ToDictionary(mod => mod.Mod);

        /// <summary>
        /// The library's files by their SHA-256, in any case.
        /// </summary>
        public Dictionary<string, (LibraryMod Mod, LibraryFile File)> Shas { get; } = library
            .SelectMany(mod => mod.Files.Where(file => file.Sha256 is not null).Select(file => (mod, file)))
            .DistinctBy(kept => kept.file.Sha256!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kept => kept.file.Sha256!, kept => (kept.mod, kept.file), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The archives read so far by their SHA-256, with the path of the first of each.
        /// </summary>
        public Dictionary<string, string> Seen { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The files planned for import so far.
        /// </summary>
        public HashSet<ModFileRef> Planned { get; } = [];

        public Dictionary<ModRef, IReadOnlyList<ModFile>?> Files { get; } = [];

        public Dictionary<ModRef, ModInfo?> Mods { get; } = [];

        public int Requests { get; set; }

        /// <summary>
        /// Why the site isn't asked any more, once it refused in a way that stops it.
        /// </summary>
        public string? Why { get; set; }
    }

    private abstract record Outcome;

    private sealed record Imported(ImportedArchive Archive, bool NewMod) : Outcome;

    private sealed record AlreadyIn : Outcome;

    private sealed record Failed(string Message) : Outcome;
}
