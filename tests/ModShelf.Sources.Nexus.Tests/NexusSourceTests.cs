using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Nexus.Tests.Common;
using ModShelf.Testing;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// The checks every source passes, for the capabilities the Nexus source has so far.
/// </summary>
public sealed class NexusSourceTests : SourceContractTests<NexusSource>, IDisposable
{
    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceTests()
    {
        _services = _api.BuildServices();
    }

    protected override SourceId ExpectedId { get; } = new("nexus");

    protected override IReadOnlyList<Uri> OwnLinks { get; } = [];

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    protected override NexusSource CreateSource() => _services.GetServices<IModSource>().OfType<NexusSource>().Single();
}
