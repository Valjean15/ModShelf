using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.ModIo.Tests.Common;
using ModShelf.Testing;

namespace ModShelf.Sources.ModIo.Tests;

/// <summary>
/// The checks every source passes, for the capabilities the mod.io source has, against the recorded mod.io, with an
/// access token linked.
/// </summary>
public sealed class ModIoSourceTests : SourceContractTests<ModIoSource>, IDisposable
{
    private readonly RecordedModIoApi _api = new();
    private readonly ServiceProvider _services;

    public ModIoSourceTests()
    {
        _api.ServeAll();
        _services = _api.BuildServices(linked: RecordedModIoApi.AccessToken);
    }

    protected override SourceId ExpectedId { get; } = new("modio");

    protected override IReadOnlyList<Uri> OwnLinks { get; } =
    [
        new($"https://mod.io/g/{RecordedModIoApi.GameName}/m/{RecordedModIoApi.ModName}"),
        new($"https://g-{RecordedModIoApi.Game}.modapi.io/v1/games/{RecordedModIoApi.Game}/mods/{RecordedModIoApi.Mod}/files/{RecordedModIoApi.LiveFile}/download"),
    ];

    protected override TimeProvider Clock => _api.Clock;

    protected override GameRef? SearchableGame { get; } = new(new SourceId("modio"), RecordedModIoApi.Game);

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    protected override ModIoSource CreateSource() => _services.GetServices<IModSource>().OfType<ModIoSource>().Single();
}
