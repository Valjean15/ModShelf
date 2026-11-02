using ModShelf.Application.Library;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A rescan that answers as the test says, recording what it was asked to rescan.
/// </summary>
internal sealed class ScriptedRescan : ILibraryRescan
{
    /// <summary>
    /// The links asked to rescan, in order; null for the whole library.
    /// </summary>
    public List<Uri?> Asked { get; } = [];

    /// <summary>
    /// What each rescan gives: nothing found, unless the test says.
    /// </summary>
    public LibraryRescan Result { get; set; } = new(1, [], [], [], [], []);

    /// <summary>
    /// What the next rescan throws instead; null for none.
    /// </summary>
    public Exception? Refusal { get; set; }

    public Task<LibraryRescan> RescanAsync(Uri? link, CancellationToken cancellationToken)
    {
        Asked.Add(link);
        return Refusal is { } refusal ? Task.FromException<LibraryRescan>(refusal) : Task.FromResult(Result);
    }
}
