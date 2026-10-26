using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Nexus.Tests.Common;
using ModShelf.Testing;

namespace ModShelf.Sources.Nexus.Tests;

/// <summary>
/// The checks every source passes, for the capabilities the Nexus source has so far, against the recorded API.
/// </summary>
public sealed class NexusSourceTests : SourceContractTests<NexusSource>, IDisposable
{
    private readonly RecordedNexusApi _api = new();
    private readonly ServiceProvider _services;

    public NexusSourceTests()
    {
        _api.Serve(RecordedNexusApi.Premium, "file");
        _api.Serve(RecordedNexusApi.Premium, "files");
        _services = _api.BuildServices();
    }

    protected override SourceId ExpectedId { get; } = new("nexus");

    protected override IReadOnlyList<Uri> OwnLinks { get; } =
    [
        new("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=160380"),
        new("https://www.nexusmods.com/stardewvalley/mods/2400"),
        new("nxm://stardewvalley/mods/2400/files/160380?key=nxm-key&expires=1893456000&user_id=1000001"),
    ];

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    protected override NexusSource CreateSource() => _services.GetServices<IModSource>().OfType<NexusSource>().Single();
}
