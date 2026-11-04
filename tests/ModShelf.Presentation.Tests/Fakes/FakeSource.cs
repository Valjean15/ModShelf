using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A mod site with only its name, and its words when the test gives them.
/// </summary>
internal sealed record FakeSource(SourceId Id, string DisplayName) : IModSource
{
    public SiteWords Words { get; init; } = SiteWords.None;
}
