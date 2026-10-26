using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A source that recognizes links the way the test says, and counts how often it's asked.
/// </summary>
internal sealed class ScriptedLinkRecognizer(SourceId id, Func<Uri, RecognizedLink?> recognize) : ILinkRecognizer
{
    private int _calls;

    public SourceId Id { get; } = id;

    public string DisplayName => Id.Value;

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>
    /// A source that recognizes every link as <paramref name="file"/>.
    /// </summary>
    public static ScriptedLinkRecognizer Always(
        ModFileRef file,
        string? suggestedFileName = "SkyUI_5_2_SE.7z",
        bool isCatchAll = false,
        bool isHandoff = false)
        => new(file.Mod.Game.Source, _ => new RecognizedLink(file, suggestedFileName, isCatchAll, isHandoff));

    /// <summary>
    /// A source that recognizes no link.
    /// </summary>
    public static ScriptedLinkRecognizer Never(SourceId id) => new(id, _ => null);

    public Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(recognize(link));
    }
}
