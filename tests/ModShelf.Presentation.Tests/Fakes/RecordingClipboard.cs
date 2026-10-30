using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A clipboard that keeps what's put on it, or refuses with <see cref="Refusal"/>.
/// </summary>
internal sealed class RecordingClipboard : IClipboard
{
    public List<string> Texts { get; } = [];

    public Exception? Refusal { get; set; }

    public Task SetTextAsync(string text, CancellationToken cancellationToken)
    {
        if (Refusal is not null)
        {
            return Task.FromException(Refusal);
        }

        Texts.Add(text);
        return Task.CompletedTask;
    }
}
