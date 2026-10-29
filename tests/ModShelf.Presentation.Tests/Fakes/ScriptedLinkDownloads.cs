using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;

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

    /// <summary>
    /// Each link handed over, with the download it was for.
    /// </summary>
    public List<(DownloadJobId Waiting, Uri Link)> HandedOver { get; } = [];

    /// <summary>
    /// What handing a link over answers.
    /// </summary>
    public HandoffResult HandoffAnswer { get; set; } = new(Application.Downloads.HandoffAnswer.Taken);

    public Task<HandoffResult> HandOverAsync(DownloadJobId waiting, Uri link, CancellationToken cancellationToken)
    {
        HandedOver.Add((waiting, link));
        return Refusals.TryDequeue(out Exception? refusal)
            ? Task.FromException<HandoffResult>(refusal)
            : Task.FromResult(HandoffAnswer);
    }

    public Task<LinkDownload> AddAsync(Uri link, string targetDirectory, string? fileName, CancellationToken cancellationToken)
    {
        Added.Add((link, targetDirectory, fileName));
        return Refusals.TryDequeue(out Exception? refusal)
            ? Task.FromException<LinkDownload>(refusal)
            : Task.FromResult(Answer(link, targetDirectory, fileName));
    }
}
