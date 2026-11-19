using ModShelf.Application.Cleanup;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Freeing up space as the test says: the entries a plan lists, and what deleting does, which by default deletes every
/// file asked. It can hold listing and deleting back until released, and fail either.
/// </summary>
internal sealed class ScriptedLibraryCleanup : ILibraryCleanup
{
    /// <summary>
    /// The files a plan lists, for any game.
    /// </summary>
    public List<CleanupEntry> Entries { get; } = [];

    /// <summary>
    /// What listing throws instead, while set.
    /// </summary>
    public Exception? PlanFailure { get; set; }

    /// <summary>
    /// What deleting throws instead, while set.
    /// </summary>
    public Exception? DeleteFailure { get; set; }

    /// <summary>
    /// The files deleting leaves, and why, by name on disk.
    /// </summary>
    public Dictionary<string, string> Left { get; } = [];

    /// <summary>
    /// Holds listing, after it says it asked about the first mod, until it's set.
    /// </summary>
    public TaskCompletionSource? HoldPlanning { get; set; }

    /// <summary>
    /// Holds deleting, after its first file, until it's set.
    /// </summary>
    public TaskCompletionSource? HoldDeleting { get; set; }

    /// <summary>
    /// The game each listing was for; null for every game.
    /// </summary>
    public List<GameRef?> Planned { get; } = [];

    /// <summary>
    /// The files each deletion was asked to delete.
    /// </summary>
    public List<IReadOnlyList<CleanupEntry>> Deletions { get; } = [];

    public async Task<CleanupPlan> PlanAsync(GameRef? game, IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
    {
        Planned.Add(game);
        progress?.Report(new CleanupProgress(1, 2));
        if (HoldPlanning is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        return PlanFailure is { } failure ? throw failure : new CleanupPlan(game, [.. Entries], 2);
    }

    public async Task<CleanupResult> DeleteAsync(IReadOnlyList<CleanupEntry> entries, IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
    {
        Deletions.Add(entries);
        progress?.Report(new CleanupProgress(1, entries.Count));
        if (HoldDeleting is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        if (DeleteFailure is { } failure)
        {
            throw failure;
        }

        return new CleanupResult(
            [.. entries.Where(entry => !Left.ContainsKey(entry.FileName))],
            [.. entries.Where(entry => Left.ContainsKey(entry.FileName)).Select(entry => new CleanupFailure(entry, Left[entry.FileName]))]);
    }
}
