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
        _api.Serve(RecordedNexusApi.Premium, "download-link");
        _api.Serve(RecordedNexusApi.Premium, "game");
        _api.Serve(RecordedNexusApi.Premium, "mod");
        _api.Serve(RecordedNexusApi.Premium, "changelogs");
        _api.Serve(RecordedNexusApi.Premium, "updated-1d");

        // When the updates were recorded, so the last day's are recent.
        _api.Time.SetUtcNow(new DateTimeOffset(2026, 10, 23, 22, 59, 0, TimeSpan.Zero));
        _services = _api.BuildServices();
    }

    protected override SourceId ExpectedId { get; } = new("nexus");

    protected override IReadOnlyList<Uri> OwnLinks { get; } =
    [
        new("https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=160380"),
        new("https://www.nexusmods.com/stardewvalley/mods/2400"),
        new("nxm://stardewvalley/mods/2400/files/160380?key=nxm-key&expires=1893456000&user_id=1000001"),
    ];

    protected override TimeProvider Clock => _api.Time;

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    protected override NexusSource CreateSource() => _services.GetServices<IModSource>().OfType<NexusSource>().Single();
}
