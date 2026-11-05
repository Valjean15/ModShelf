using System.Collections.Immutable;

using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Moves the library by changing its root in the settings, as the real mover does once the files are moved, recording
/// each move; or refuses, as the test says. A game's move plans 3 files of 1,000 bytes each, on the same drive, unless
/// the test changes the plan, tells how far it is, and can wait part-way until the test lets it go or it's stopped.
/// </summary>
internal sealed class ScriptedLibraryMover(InMemorySettingsService settings) : ILibraryMover
{
    public List<string> Moves { get; } = [];

    /// <summary>
    /// What moving, and planning a game's move, throws while set.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// What a game's move throws while set, once it's part-way, after its plan was made.
    /// </summary>
    public Exception? MoveRefusal { get; set; }

    /// <summary>
    /// How many of the library's files each move finds missing.
    /// </summary>
    public int Missing { get; set; }

    /// <summary>
    /// Changes the plan of a game's move, such as to another drive without room; null for the plan as it is.
    /// </summary>
    public Func<GameMovePlan, GameMovePlan>? Plan { get; set; }

    /// <summary>
    /// While set, planning a game's move waits for it.
    /// </summary>
    public TaskCompletionSource? PlanHold { get; set; }

    /// <summary>
    /// While set, a game's move waits for it once <see cref="HoldAfter"/> files have moved, unless it's stopped.
    /// </summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>
    /// How many files a game's move moves before it waits for <see cref="Hold"/>.
    /// </summary>
    public int HoldAfter { get; set; } = 1;

    public Task<LibraryMove> MoveAsync(string root, CancellationToken cancellationToken)
    {
        Moves.Add(root);
        if (Refusal is { } refusal)
        {
            return Task.FromException<LibraryMove>(refusal);
        }

        string from = settings.Stored.LibraryRoot ?? settings.DefaultLibraryRoot;
        settings.Stored = settings.Stored with { LibraryRoot = root };
        return Task.FromResult(new LibraryMove(from, root, 3, 0, Missing));
    }

    public async Task<GameMovePlan> PlanGameMoveAsync(GameRef game, string? folder, CancellationToken cancellationToken)
    {
        if (PlanHold is { } hold)
        {
            await hold.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        if (Refusal is { } refusal)
        {
            throw refusal;
        }

        LibraryRoots roots = LibraryRoots.Of(settings.Stored, settings.DefaultLibraryRoot);
        GameMovePlan plan = new(game, roots.For(game), folder ?? roots.Root, 3, 3000, 0, Missing, null, SameDrive: true);
        return Plan?.Invoke(plan) ?? plan;
    }

    public async Task<LibraryMove> MoveGameAsync(GameRef game, string? folder, IProgress<LibraryMoveProgress>? progress, CancellationToken cancellationToken)
    {
        Moves.Add($"{game} {folder ?? "back"}");
        GameMovePlan plan = await PlanGameMoveAsync(game, folder, cancellationToken);
        long each = plan.Files == 0 ? 0 : plan.Bytes / plan.Files;
        progress?.Report(new LibraryMoveProgress(0, plan.Files, 0, plan.Bytes));
        int moved = Math.Min(HoldAfter, plan.Files);
        for (int file = 1; file <= moved; file++)
        {
            progress?.Report(new LibraryMoveProgress(file, plan.Files, file * each, plan.Bytes));
        }

        if (Hold is { } hold)
        {
            try
            {
                await hold.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException($"The move was stopped. Every file is back where it was, and {game} stays in {plan.From}.", null, cancellationToken);
            }
        }

        if (MoveRefusal is { } refusal)
        {
            throw refusal;
        }

        progress?.Report(new LibraryMoveProgress(plan.Files, plan.Files, plan.Bytes, plan.Bytes));
        LibraryRoots roots = LibraryRoots.Of(settings.Stored, settings.DefaultLibraryRoot);
        GameSettings own = settings.Stored.GameFor(game) with { Root = folder is null || folder == roots.Root ? null : folder };
        settings.Stored = settings.Stored with { Games = settings.Stored.Games.ToImmutableDictionary(AppSettings.GameComparer).SetItem(game, own) };
        return new LibraryMove(plan.From, plan.To, plan.Files, plan.AlreadyThere, plan.Missing, plan.CopiesKept);
    }
}
