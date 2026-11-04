using System.Globalization;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Domain.Common;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// Moves the library's files from root to root, or a game's from folder to folder, file by file, and changes the root
/// or the game's folder in the settings once every file is there. Library paths are relative to the root, or the game's
/// folder, so the library itself doesn't change.
/// </summary>
/// <remarks>
/// It holds the library (<see cref="ILibraryHold"/>) throughout, so no file is placed or recorded in this process
/// meanwhile. A download another process starts into the old root meanwhile undoes the move before the root changes.
/// </remarks>
internal sealed partial class LibraryMover : ILibraryMover
{
    private readonly SettingsService _settings;
    private readonly ILibraryRepository _library;
    private readonly ILibraryStorage _storage;
    private readonly ILibraryHold _hold;
    private readonly ILogger<LibraryMover> _logger;

    public LibraryMover(
        SettingsService settings,
        ILibraryRepository library,
        ILibraryStorage storage,
        ILibraryHold hold,
        ILogger<LibraryMover> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _library = library;
        _storage = storage;
        _hold = hold;
        _logger = logger;
    }

    public async Task<LibraryMove> MoveAsync(string root, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string to = _settings.FullRoot(root, nameof(root));
        using IDisposable hold = await _hold.HoldAsync(cancellationToken).ConfigureAwait(false);
        LibraryRoots roots = await _settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
        string from = roots.Root;
        // Under another name, the files would be in their own way.
        if (string.Equals(from, to, StringComparison.Ordinal) || _storage.IsSameFolder(from, to))
        {
            return new LibraryMove(from, from, 0, 0, 0);
        }

        if (LibraryPaths.RelativeTo(from, to) is not null)
        {
            throw new InvalidOperationException($"{to} is inside the library, which is in {from}. Choose a folder outside it.");
        }

        if (LibraryPaths.RelativeTo(to, from) is not null)
        {
            throw new InvalidOperationException($"{to} holds the library, which is in {from}. Choose a folder that doesn't.");
        }

        if (roots.ClashOf(null, to) is { } clash)
        {
            throw new InvalidOperationException(clash);
        }

        await _settings.EnsureNoDownloadsIntoAsync(from, null, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LibraryMod> library = await _library.ListAsync(cancellationToken).ConfigureAwait(false);
        Plan plan = PlanMove(from, to, [.. library.Where(mod => !roots.HasOwn(mod.Mod.Game)).SelectMany(mod => mod.Files)], adopt: false);
        if (plan.Blocked.Count > 0)
        {
            string others = plan.Blocked.Count == 1 ? string.Empty : $", and {plan.Blocked.Count - 1} more like it";
            throw new InvalidOperationException(
                $"A file is at {plan.Blocked[0]}{others}, where one of the library's would go. If a move stopped part-way, it may be "
                + "a partial copy. Move or delete it, then move the library again.");
        }

        EnsurePathsFit(plan);
        List<(string From, string To)> moved = [];
        foreach ((string source, string destination, _) in plan.ToMove)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _storage.MoveFile(source, destination);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                int stuck = MoveBack(moved, to);
                LogFailed(_logger, exception, source, moved.Count);
                throw new IOException($"{source} couldn't be moved: {exception.Message} {Outcome(stuck, from, to)}", exception);
            }

            moved.Add((source, destination));
        }

        // A download another process started into the old root meanwhile would be saved where the library isn't.
        try
        {
            await _settings.EnsureNoDownloadsIntoAsync(from, null, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            int stuck = MoveBack(moved, to);
            LogDownloadStarted(_logger, from, moved.Count);
            throw new InvalidOperationException($"{exception.Message} {Outcome(stuck, from, to)}", exception);
        }

        // Settings that can't be saved would leave the library's files where it isn't.
        try
        {
            await _settings.MoveLibraryRootAsync(to, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            int stuck = MoveBack(moved, to);
            LogCantSaveRoot(_logger, exception, to, moved.Count);
            throw new IOException($"The library's new root couldn't be saved: {exception.Message} {Outcome(stuck, from, to)}", exception);
        }

        RemoveEmptiedFolders(from, plan.ToMove.Select(file => file.From), removeRoot: true);
        LogMoved(_logger, from, to, moved.Count);
        return new LibraryMove(from, to, moved.Count, plan.AlreadyThere, plan.Missing);
    }

    public async Task<GameMovePlan> PlanGameMoveAsync(GameRef game, string? folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        string? normalized = folder is null ? null : _settings.FullRoot(folder, nameof(folder));
        LibraryRoots roots = await _settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
        return (await PlanGameAsync(roots, game, normalized, cancellationToken).ConfigureAwait(false)).Summary;
    }

    public async Task<LibraryMove> MoveGameAsync(
        GameRef game,
        string? folder,
        IProgress<LibraryMoveProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        string? normalized = folder is null ? null : _settings.FullRoot(folder, nameof(folder));
        using IDisposable hold = await _hold.HoldAsync(cancellationToken).ConfigureAwait(false);
        LibraryRoots roots = await _settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false);
        normalized = OwnFolder(roots, normalized);
        (GameMovePlan summary, Plan plan) = await PlanGameAsync(roots, game, normalized, cancellationToken).ConfigureAwait(false);
        (string from, string to) = (summary.From, summary.To);
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return new LibraryMove(from, from, 0, 0, 0);
        }

        if (!summary.Fits)
        {
            throw new InvalidOperationException(
                $"{game}'s files take {Size(summary.Bytes)}, and the drive that holds {to} has {Size(summary.FreeSpace ?? 0)} free. "
                + "Make room there, or choose another folder.");
        }

        List<(string From, string To)> moved = [];
        long movedBytes = 0;
        progress?.Report(new LibraryMoveProgress(0, summary.Files, 0, summary.Bytes));
        foreach ((string source, string destination, long bytes) in plan.ToMove)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                int stuck = MoveBack(moved, to);
                LogGameMoveStopped(_logger, game, moved.Count);
                throw new OperationCanceledException($"The move was stopped. {GameOutcome(stuck, game, from, to)}", null, cancellationToken);
            }

            try
            {
                _storage.MoveFile(source, destination);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                int stuck = MoveBack(moved, to);
                LogFailed(_logger, exception, source, moved.Count);
                throw new IOException($"{source} couldn't be moved: {exception.Message} {GameOutcome(stuck, game, from, to)}", exception);
            }

            moved.Add((source, destination));
            movedBytes += bytes;
            progress?.Report(new LibraryMoveProgress(moved.Count, summary.Files, movedBytes, summary.Bytes));
        }

        // A download another process started into the old folder meanwhile would be saved where the game isn't.
        try
        {
            await _settings.EnsureNoDownloadsIntoAsync(from, game, CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            int stuck = MoveBack(moved, to);
            LogDownloadStarted(_logger, from, moved.Count);
            throw new InvalidOperationException($"{exception.Message} {GameOutcome(stuck, game, from, to)}", exception);
        }

        // Settings that can't be saved would leave the game's files where it isn't.
        try
        {
            await _settings.MoveGameRootAsync(game, normalized, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            int stuck = MoveBack(moved, to);
            LogCantSaveRoot(_logger, exception, to, moved.Count);
            throw new IOException($"{game}'s new folder couldn't be saved: {exception.Message} {GameOutcome(stuck, game, from, to)}", exception);
        }

        // The library's folder stays, even when the game took the last files from it.
        RemoveEmptiedFolders(from, plan.ToMove.Select(file => file.From), removeRoot: !FilePaths.Same(from, roots.Root));
        LogGameMoved(_logger, game, from, to, moved.Count);
        return new LibraryMove(from, to, moved.Count, plan.AlreadyThere, plan.Missing, plan.CopiesKept);
    }

    /// <summary>
    /// What undoing a move left, as a sentence.
    /// </summary>
    /// <param name="stuck">How many files couldn't go back.</param>
    private static string Outcome(int stuck, string from, string to) => stuck switch
    {
        0 => $"Every file is back where it was, and the library stays in {from}.",
        1 => $"1 file couldn't go back, so it's in {to}. Move the library there again to finish the move.",
        _ => $"{stuck} files couldn't go back, so they're in {to}. Move the library there again to finish the move.",
    };

    /// <summary>
    /// What undoing a game's move left, as a sentence.
    /// </summary>
    /// <param name="stuck">How many files couldn't go back.</param>
    private static string GameOutcome(int stuck, GameRef game, string from, string to) => stuck switch
    {
        0 => $"Every file is back where it was, and {game} stays in {from}.",
        1 => $"1 file couldn't go back, so it's in {to}. Move {game} there again to finish the move.",
        _ => $"{stuck} files couldn't go back, so they're in {to}. Move {game} there again to finish the move.",
    };

    /// <summary>
    /// A number of bytes in a few words, such as "3.4 GB".
    /// </summary>
    private static string Size(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{bytes:N0} bytes")
            : string.Create(CultureInfo.CurrentCulture, $"{size:0.#} {units[unit]}");
    }

    /// <summary>
    /// What moving <paramref name="game"/>'s files to <paramref name="folder"/> would do, with the files it would move.
    /// </summary>
    /// <param name="folder">The new folder, as it's stored; null for the library's root.</param>
    private async Task<(GameMovePlan Summary, Plan Plan)> PlanGameAsync(
        LibraryRoots roots,
        GameRef game,
        string? folder,
        CancellationToken cancellationToken)
    {
        string from = roots.For(game);
        string? own = OwnFolder(roots, folder);
        string to = own ?? roots.Root;
        if (string.Equals(from, to, StringComparison.Ordinal) || _storage.IsSameFolder(from, to))
        {
            return (new GameMovePlan(game, from, from, 0, 0, 0, 0, _storage.FreeSpace(from), SameDrive: true), new Plan([], [], 0, 0, 0));
        }

        if (own is not null && roots.ClashOf(game, own) is { } clash)
        {
            throw new InvalidOperationException(clash);
        }

        await _settings.EnsureNoDownloadsIntoAsync(from, game, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LibraryMod> library = await _library.ListAsync(cancellationToken).ConfigureAwait(false);
        Plan plan = PlanMove(
            from,
            to,
            [.. library.Where(mod => AppSettings.GameComparer.Equals(mod.Mod.Game, game)).SelectMany(mod => mod.Files)],
            adopt: true);
        if (plan.Blocked.Count > 0)
        {
            string others = plan.Blocked.Count == 1 ? string.Empty : $", and {plan.Blocked.Count - 1} more like it";
            throw new InvalidOperationException(
                $"A file is at {plan.Blocked[0]}{others}, where one of {game}'s would go, and it isn't the same size. Move or delete it, "
                + $"then move {game} again.");
        }

        EnsurePathsFit(plan);
        return (
            new GameMovePlan(
                game,
                from,
                to,
                plan.ToMove.Count,
                plan.ToMove.Sum(file => file.Bytes),
                plan.AlreadyThere,
                plan.Missing,
                _storage.FreeSpace(to),
                _storage.SameDrive(from, to),
                plan.CopiesKept),
            plan);
    }

    /// <summary>
    /// The folder a game would have of its own: none for the library's root named as one, which is the game going back
    /// into the library.
    /// </summary>
    private static string? OwnFolder(LibraryRoots roots, string? folder)
        => folder is not null && FilePaths.Same(folder, roots.Root) ? null : folder;

    /// <summary>
    /// Checks every file's new path before any file moves, so a path too long for this system stops the move with
    /// every file in place.
    /// </summary>
    /// <exception cref="InvalidOperationException">A path is too long.</exception>
    private void EnsurePathsFit(Plan plan)
    {
        foreach ((_, string destination, _) in plan.ToMove)
        {
            try
            {
                _storage.EnsurePathFits(destination);
            }
            catch (PathTooLongException exception)
            {
                throw new InvalidOperationException(exception.Message, exception);
            }
        }
    }

    /// <summary>
    /// Where each file is and goes. A file at the new root and gone from the old one, at its size, was moved by a move
    /// that stopped part-way; a different file there blocks the move.
    /// </summary>
    /// <param name="adopt">
    /// Whether a file at the new root at its size counts as there even while the old root has it too, whose copy then
    /// stays, as when a game's folder is one the user copied its files to.
    /// </param>
    private Plan PlanMove(string from, string to, IReadOnlyList<LibraryFile> files, bool adopt)
    {
        List<(string From, string To, long Bytes)> toMove = [];
        List<string> blocked = [];
        int alreadyThere = 0;
        int missing = 0;
        int copiesKept = 0;
        foreach (LibraryFile file in files)
        {
            string source = LibraryPaths.Resolve(from, file.RelativePath);
            string destination = LibraryPaths.Resolve(to, file.RelativePath);
            long? atSource = _storage.FileSize(source);
            long? atDestination = _storage.FileSize(destination);
            if (atDestination is { } size)
            {
                if (size != file.SizeInBytes || (atSource is not null && !adopt))
                {
                    blocked.Add(destination);
                }
                else
                {
                    alreadyThere++;
                    copiesKept += atSource is null ? 0 : 1;
                }
            }
            else if (atSource is { } bytes)
            {
                toMove.Add((source, destination, bytes));
            }
            else
            {
                missing++;
            }
        }

        return new Plan(toMove, blocked, alreadyThere, missing, copiesKept);
    }

    /// <summary>
    /// Moves files back where they came from, newest first, and removes the folders that leaves empty under the new root.
    /// </summary>
    /// <returns>How many couldn't go back.</returns>
    private int MoveBack(List<(string From, string To)> moved, string to)
    {
        int stuck = 0;
        for (int index = moved.Count - 1; index >= 0; index--)
        {
            try
            {
                _storage.MoveFile(moved[index].To, moved[index].From);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                stuck++;
                LogCantMoveBack(_logger, exception, moved[index].To);
            }
        }

        RemoveEmptiedFolders(to, moved.Select(file => file.To), removeRoot: true);
        return stuck;
    }

    /// <summary>
    /// Removes the folders that held <paramref name="files"/> under <paramref name="root"/>, as far up as they're empty,
    /// and the root too when <paramref name="removeRoot"/> says so.
    /// </summary>
    private void RemoveEmptiedFolders(string root, IEnumerable<string> files, bool removeRoot)
    {
        foreach (string folder in files.Select(Path.GetDirectoryName).OfType<string>().Distinct().OrderByDescending(folder => folder.Length))
        {
            for (string? current = folder;
                current is not null && ((removeRoot && FilePaths.Same(current, root)) || LibraryPaths.RelativeTo(root, current) is not null);
                current = Path.GetDirectoryName(current))
            {
                if (!_storage.RemoveFolderIfEmpty(current))
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// What a move does with each of the library's files.
    /// </summary>
    /// <param name="ToMove">Files at the old root, to move, with their sizes.</param>
    /// <param name="Blocked">Where another file is in the way.</param>
    /// <param name="AlreadyThere">How many are at the new root already.</param>
    /// <param name="Missing">How many are at neither.</param>
    /// <param name="CopiesKept">Of those already there, how many the old root has too.</param>
    private sealed record Plan(
        IReadOnlyList<(string From, string To, long Bytes)> ToMove,
        IReadOnlyList<string> Blocked,
        int AlreadyThere,
        int Missing,
        int CopiesKept);
}
