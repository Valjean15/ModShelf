using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Library;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Downloads into the library that answer as the test says, recording each link added.
/// </summary>
internal sealed class ScriptedLibraryDownloads : ILibraryDownloads
{
    public List<Uri> Added { get; } = [];

    /// <summary>
    /// What adding a link returns.
    /// </summary>
    public Func<Uri, LibraryDownload> Answer { get; set; } = _ => throw new InvalidOperationException("The test gave no answer.");

    /// <summary>
    /// What the next adds throw, one each, before any answers.
    /// </summary>
    public Queue<Exception> Refusals { get; } = new();

    public Task<LibraryDownload> AddAsync(Uri link, CancellationToken cancellationToken)
    {
        Added.Add(link);
        return Refusals.TryDequeue(out Exception? refusal) ? Task.FromException<LibraryDownload>(refusal) : Task.FromResult(Answer(link));
    }

    public Task<LibraryRecord?> RecordAsync(DownloadJobSnapshot job, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RecordMissedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
