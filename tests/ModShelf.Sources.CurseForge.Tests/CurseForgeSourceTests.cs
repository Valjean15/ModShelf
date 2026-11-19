using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.CurseForge.Tests.Common;
using ModShelf.Testing;

namespace ModShelf.Sources.CurseForge.Tests;

/// <summary>
/// The checks every source passes, for the capabilities the CurseForge source has, against the recorded CurseForge: its
/// games, a game, and one of its mods with its files, its main file and the mods that file needs, the mods a search
/// found, searched again as each check asks, and the mod found by its slug (ADR 0107).
/// </summary>
public sealed class CurseForgeSourceTests : SourceContractTests<CurseForgeSource>, IDisposable
{
    private readonly CurseForgeServer _server = new();
    private readonly ServiceProvider _services;

    public CurseForgeSourceTests()
    {
        _server.ServeRecordings();
        _services = _server.BuildServices();
    }

    /// <summary>
    /// The recorded mod's page, as CurseForge gives it.
    /// </summary>
    internal static Uri ModPage { get; } = new(CurseForgeServer.Body("mod")["data"]!["links"]!["websiteUrl"]!.GetValue<string>().TrimEnd('/'));

    /// <summary>
    /// The recorded mod's main file.
    /// </summary>
    internal static string MainFile { get; } = CurseForgeServer.Body("mod")["data"]!["mainFileId"]!.GetValue<long>().ToString(System.Globalization.CultureInfo.InvariantCulture);

    protected override SourceId ExpectedId { get; } = new("curseforge");

    protected override GameRef? SearchableGame { get; } = new(new SourceId("curseforge"), ModPage.Segments[1].TrimEnd('/'));

    protected override IReadOnlyList<Uri> OwnLinks { get; } =
    [
        ModPage,
        new($"{ModPage}/files/{MainFile}"),
        new($"{ModPage}/download/{MainFile}"),
    ];

    public void Dispose()
    {
        _services.Dispose();
        _server.Dispose();
    }

    protected override CurseForgeSource CreateSource() => CurseForgeServer.SourceOf(_services);
}
