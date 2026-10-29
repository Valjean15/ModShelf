using ModShelf.Application.Library;

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
}
