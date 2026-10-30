using ModShelf.Application.Links;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Opens the links other apps hand over as the test says, recording each.
/// </summary>
internal sealed class ScriptedOpenedLinks : IOpenedLinks
{
    public List<Uri> Opened { get; } = [];

    /// <summary>
    /// What opening a link returns.
    /// </summary>
    public Func<Uri, OpenedLink> Answer { get; set; } = _ => throw new InvalidOperationException("The test gave no answer.");

    /// <summary>
    /// What the next opens throw, one each, before any answers.
    /// </summary>
    public Queue<Exception> Refusals { get; } = new();

    public Task<OpenedLink> OpenAsync(Uri link, CancellationToken cancellationToken)
    {
        Opened.Add(link);
        return Refusals.TryDequeue(out Exception? refusal) ? Task.FromException<OpenedLink>(refusal) : Task.FromResult(Answer(link));
    }
}
