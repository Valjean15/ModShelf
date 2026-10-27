using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A mod site with only its name.
/// </summary>
internal sealed record FakeSource(SourceId Id, string DisplayName) : IModSource;
