using System.Collections.Immutable;

using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Moves the library by changing its root in the settings, as the real mover does once the files are moved, recording
/// each move; or refuses, as the test says.
/// </summary>
internal sealed class ScriptedLibraryMover(InMemorySettingsService settings) : ILibraryMover
{
    public List<string> Moves { get; } = [];

    public Exception? Refusal { get; set; }

    /// <summary>
    /// How many of the library's files each move finds missing.
    /// </summary>
    public int Missing { get; set; }

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

    public Task<GameMovePlan> PlanGameMoveAsync(GameRef game, string? folder, CancellationToken cancellationToken)
    {
        LibraryRoots roots = LibraryRoots.Of(settings.Stored, settings.DefaultLibraryRoot);
        return Refusal is { } refusal
            ? Task.FromException<GameMovePlan>(refusal)
            : Task.FromResult(new GameMovePlan(game, roots.For(game), folder ?? roots.Root, 3, 3000, 0, Missing, null, SameDrive: true));
    }

    public Task<LibraryMove> MoveGameAsync(GameRef game, string? folder, IProgress<LibraryMoveProgress>? progress, CancellationToken cancellationToken)
    {
        Moves.Add($"{game} {folder ?? "back"}");
        if (Refusal is { } refusal)
        {
            return Task.FromException<LibraryMove>(refusal);
        }

        LibraryRoots roots = LibraryRoots.Of(settings.Stored, settings.DefaultLibraryRoot);
        string from = roots.For(game);
        GameSettings own = settings.Stored.GameFor(game) with { Root = folder };
        settings.Stored = settings.Stored with { Games = settings.Stored.Games.ToImmutableDictionary(AppSettings.GameComparer).SetItem(game, own) };
        return Task.FromResult(new LibraryMove(from, folder ?? roots.Root, 3, 0, Missing));
    }
}
