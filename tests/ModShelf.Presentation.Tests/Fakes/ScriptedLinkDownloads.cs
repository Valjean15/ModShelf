using ModShelf.Application.Downloads;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Downloads from links into folders that answer as the test says, recording each link added.
/// </summary>
internal sealed class ScriptedLinkDownloads : ILinkDownloads
{
    public List<(Uri Link, string Folder, string? FileName)> Added { get; } = [];

    /// <summary>
    /// What adding a link returns, given what it was added with.
    /// </summary>
    public Func<Uri, string, string?, LinkDownload> Answer { get; set; } = (_, _, _) => throw new InvalidOperationException("The test gave no answer.");

    /// <summary>
    /// What the next adds throw, one each, before any answers.
    /// </summary>
    public Queue<Exception> Refusals { get; } = new();

    public Task<LinkDownload> AddAsync(Uri link, string targetDirectory, string? fileName, CancellationToken cancellationToken)
    {
        Added.Add((link, targetDirectory, fileName));
        return Refusals.TryDequeue(out Exception? refusal)
            ? Task.FromException<LinkDownload>(refusal)
            : Task.FromResult(Answer(link, targetDirectory, fileName));
    }
}
