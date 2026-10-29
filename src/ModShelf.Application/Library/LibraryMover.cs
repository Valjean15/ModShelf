using Microsoft.Extensions.Logging;

using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;

namespace ModShelf.Application.Library;

/// <summary>
/// Moves the library's files from root to root, file by file, and changes the root in the settings once every file is
/// there. Library paths are relative to the root, so the library itself doesn't change.
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
        if (!Path.IsPathFullyQualified(root.Trim()))
        {
            throw new ArgumentException($"'{root}' isn't a full path, such as {_settings.DefaultLibraryRoot}.", nameof(root));
        }

        string to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Trim()));
        using IDisposable hold = await _hold.HoldAsync(cancellationToken).ConfigureAwait(false);
        string from = await _settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
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

        await _settings.EnsureNoDownloadsIntoAsync(from, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LibraryMod> library = await _library.ListAsync(cancellationToken).ConfigureAwait(false);
        Plan plan = PlanMove(from, to, [.. library.SelectMany(mod => mod.Files)]);
        if (plan.Blocked.Count > 0)
        {
            string others = plan.Blocked.Count == 1 ? string.Empty : $", and {plan.Blocked.Count - 1} more like it";
            throw new InvalidOperationException(
                $"A file is at {plan.Blocked[0]}{others}, where one of the library's would go. If a move stopped part-way, it may be "
                + "a partial copy. Move or delete it, then move the library again.");
        }

        List<(string From, string To)> moved = [];
        foreach ((string source, string destination) in plan.ToMove)
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
            await _settings.EnsureNoDownloadsIntoAsync(from, cancellationToken).ConfigureAwait(false);
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

        RemoveEmptiedFolders(from, plan.ToMove.Select(file => file.From));
        LogMoved(_logger, from, to, moved.Count);
        return new LibraryMove(from, to, moved.Count, plan.AlreadyThere, plan.Missing);
    }

    /// <summary>
    /// Whether two paths name the same folder: ignoring case on Windows and macOS, whose usual file systems do.
    /// </summary>
    private static bool SamePath(string first, string second)
        => string.Equals(
            first,
            second,
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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
    /// Where each file is and goes. A file at the new root and gone from the old one, at its size, was moved by a move
    /// that stopped part-way; a different file there blocks the move.
    /// </summary>
    private Plan PlanMove(string from, string to, IReadOnlyList<LibraryFile> files)
    {
        List<(string From, string To)> toMove = [];
        List<string> blocked = [];
        int alreadyThere = 0;
        int missing = 0;
        foreach (LibraryFile file in files)
        {
            string source = LibraryPaths.Resolve(from, file.RelativePath);
            string destination = LibraryPaths.Resolve(to, file.RelativePath);
            bool atSource = _storage.FileExists(source);
            long? atDestination = _storage.FileSize(destination);
            if (atDestination is { } size && (atSource || size != file.SizeInBytes))
            {
                blocked.Add(destination);
            }
            else if (atDestination is not null)
            {
                alreadyThere++;
            }
            else if (atSource)
            {
                toMove.Add((source, destination));
            }
            else
            {
                missing++;
            }
        }

        return new Plan(toMove, blocked, alreadyThere, missing);
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

        RemoveEmptiedFolders(to, moved.Select(file => file.To));
        return stuck;
    }

    /// <summary>
    /// Removes the folders that held <paramref name="files"/> under <paramref name="root"/>, the root too, as far up as
    /// they're empty.
    /// </summary>
    private void RemoveEmptiedFolders(string root, IEnumerable<string> files)
    {
        foreach (string folder in files.Select(Path.GetDirectoryName).OfType<string>().Distinct().OrderByDescending(folder => folder.Length))
        {
            for (string? current = folder;
                current is not null && (SamePath(current, root) || LibraryPaths.RelativeTo(root, current) is not null);
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
    /// <param name="ToMove">Files at the old root, to move.</param>
    /// <param name="Blocked">Where another file is in the way.</param>
    /// <param name="AlreadyThere">How many are at the new root already.</param>
    /// <param name="Missing">How many are at neither.</param>
    private sealed record Plan(IReadOnlyList<(string From, string To)> ToMove, IReadOnlyList<string> Blocked, int AlreadyThere, int Missing);
}
