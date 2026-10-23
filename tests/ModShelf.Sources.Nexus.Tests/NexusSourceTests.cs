using ModShelf.Domain.Mods;
using ModShelf.Testing;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// The checks every source passes. The source implements no capabilities yet, so most are skipped.
/// </summary>
public sealed class NexusSourceTests : SourceContractTests<NexusSource>
{
    protected override SourceId ExpectedId { get; } = new("nexus");

    protected override IReadOnlyList<Uri> OwnLinks { get; } = [];

    protected override NexusSource CreateSource() => new();
}
