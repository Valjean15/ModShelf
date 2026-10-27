using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A file manager that opens folders or not, as the test says, recording each folder.
/// </summary>
internal sealed class ScriptedFolders : IFolderOpener
{
    public List<string> Opened { get; } = [];

    public bool CanOpen { get; set; } = true;

    public Task<bool> TryOpenAsync(string folder, CancellationToken cancellationToken)
    {
        Opened.Add(folder);
        return Task.FromResult(CanOpen);
    }
}
