using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.DegMods.Links;
using ModShelf.Sources.DegMods.Tests.Common;
using ModShelf.Testing;

namespace ModShelf.Sources.DegMods.Tests;

/// <summary>
/// The checks every source passes, for the capabilities the DEG Mods source has, against the recorded relays, with the
/// made-up game to search.
/// </summary>
public sealed class DegModsSourceTests : SourceContractTests<DegModsSource>, IDisposable
{
    private readonly RecordedDegMods _degMods = new();
    private readonly ServiceProvider _services;

    public DegModsSourceTests()
    {
        _degMods.AddTestGame();
        _services = _degMods.BuildServices();
    }

    protected override SourceId ExpectedId { get; } = new("degmods");

    protected override IReadOnlyList<Uri> OwnLinks { get; } =
    [
        DegModsLink.ModPage(DegModsOptions.DefaultSiteAddress, DegModsLink.ModKind, RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers),
        new($"nostr:{Nip19.Naddr(DegModsLink.OldModKind, RecordedDegMods.VgbAuthor, RecordedDegMods.FedAndHydration)}"),
        DegModsLink.FilePage(DegModsOptions.DefaultSiteAddress, DegModsLink.ModKind, RecordedDegMods.WifiAuthor, RecordedDegMods.RejectWifi, "8fd6e5a7d1b79081"),
    ];

    protected override GameRef? SearchableGame { get; } = new(new SourceId("degmods"), RecordedDegMods.TestGame.ToLowerInvariant());

    protected override TimeProvider Clock => _services.GetRequiredService<TimeProvider>();

    public void Dispose()
    {
        _services.Dispose();
        _degMods.Dispose();
    }

    protected override DegModsSource CreateSource() => _services.GetServices<IModSource>().OfType<DegModsSource>().Single();
}
