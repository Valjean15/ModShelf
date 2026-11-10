using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.GameBanana.Tests.Common;
using ModShelf.Testing;

namespace ModShelf.Sources.GameBanana.Tests;

/// <summary>
/// The checks every source passes, for the capabilities the GameBanana source has, against the recorded GameBanana.
/// </summary>
public sealed class GameBananaSourceTests : SourceContractTests<GameBananaSource>, IDisposable
{
    private readonly RecordedGameBananaApi _api = new();
    private readonly ServiceProvider _services;

    public GameBananaSourceTests()
    {
        _api.ServeAll();
        _services = _api.BuildServices();
    }

    protected override SourceId ExpectedId { get; } = new("gamebanana");

    protected override IReadOnlyList<Uri> OwnLinks { get; } =
    [
        new($"https://gamebanana.com/mods/{RecordedGameBananaApi.Mod}"),
        new($"https://gamebanana.com/mods/download/{RecordedGameBananaApi.Mod}"),
        new($"https://gamebanana.com/mmdl/{RecordedGameBananaApi.ArchivedFile},Mod,{RecordedGameBananaApi.Mod}"),
        new($"jasm-plus:https://gamebanana.com/mmdl/{RecordedGameBananaApi.MainFile},Mod,{RecordedGameBananaApi.Mod}"),
    ];

    protected override GameRef? SearchableGame { get; } = new(new SourceId("gamebanana"), RecordedGameBananaApi.Game);

    public void Dispose()
    {
        _services.Dispose();
        _api.Dispose();
    }

    protected override GameBananaSource CreateSource() => _services.GetServices<IModSource>().OfType<GameBananaSource>().Single();
}
