using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Library downloads that record each link asked for and queue it, unless the test says the library has its file or
/// it's refused.
/// </summary>
internal sealed class RecordingLibraryDownloads : ILibraryDownloads
{
    public List<Uri> Added { get; } = [];

    /// <summary>
    /// The links whose files the library has already.
    /// </summary>
    public HashSet<Uri> InLibrary { get; } = [];

    /// <summary>
    /// What adding a link throws instead.
    /// </summary>
    public Dictionary<Uri, Exception> Refusals { get; } = [];

    public Task<LibraryDownload> AddAsync(Uri link, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Added.Add(link);
        if (Refusals.TryGetValue(link, out Exception? refusal))
        {
            return Task.FromException<LibraryDownload>(refusal);
        }

        string path = Path.Combine(Path.GetTempPath(), "library", link.Segments[^1]);
        if (InLibrary.Contains(link))
        {
            return Task.FromResult(new LibraryDownload(path, null));
        }

        DownloadJobSnapshot job = RecordingDownloadService.Job(
            new ModFileRef(FakeModSite.Mod(link.Segments[^2].TrimEnd('/')), link.Segments[^1]), Path.GetDirectoryName(path)!, link.Segments[^1], DownloadJobStatus.Queued);
        return Task.FromResult(new LibraryDownload(path, new LinkDownload(job, AlreadyQueued: false)));
    }

    public Task<LibraryRecord?> RecordAsync(DownloadJobSnapshot job, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RecordMissedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
