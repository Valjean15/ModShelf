using ModShelf.Application.Contents;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Archives' contents as the test says: each file's listing and text files, or what listing or reading throws.
/// </summary>
internal sealed class ScriptedContents : ILibraryContents
{
    public Dictionary<ModFileRef, ArchiveContents> Listings { get; } = [];

    public Dictionary<(ModFileRef File, string Entry), ArchiveText> Texts { get; } = [];

    /// <summary>
    /// What listing throws; null for nothing.
    /// </summary>
    public Exception? ListFailure { get; set; }

    /// <summary>
    /// What reading a text file throws; null for nothing.
    /// </summary>
    public Exception? ReadFailure { get; set; }

    /// <summary>
    /// While set, reading a text file waits for it.
    /// </summary>
    public TaskCompletionSource? ReadHeld { get; set; }

    /// <summary>
    /// The text files read, as asked for.
    /// </summary>
    public List<string> Read { get; } = [];

    public bool CanList(string fileName)
        => Path.GetExtension(fileName).ToUpperInvariant() is ".ZIP" or ".7Z" or ".RAR";

    public Task<LibraryMod> FindModAsync(Uri link, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ArchiveContents> ListAsync(ModFileRef file, CancellationToken cancellationToken)
        => ListFailure is { } failure ? Task.FromException<ArchiveContents>(failure) : Task.FromResult(Listings[file]);

    public async Task<ArchiveText> ReadTextAsync(ModFileRef file, string entry, CancellationToken cancellationToken)
    {
        Read.Add(entry);
        if (ReadHeld is { } held)
        {
            await held.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ReadFailure is { } failure ? throw failure : Texts[(file, entry)];
    }
}
