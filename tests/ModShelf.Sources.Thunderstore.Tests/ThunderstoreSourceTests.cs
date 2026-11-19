using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Thunderstore.Tests.Common;
using ModShelf.Testing;

namespace ModShelf.Sources.Thunderstore.Tests;

/// <summary>
/// The checks every source passes, for the capabilities the Thunderstore source has, against the recorded Thunderstore:
/// a community of more than 100 packages, read in one chunk, and one of its packages, with its links in the community,
/// its download link and its <c>ror2mm://</c> link.
/// </summary>
public sealed class ThunderstoreSourceTests : SourceContractTests<ThunderstoreSource>, IDisposable
{
    private readonly ThunderstoreServer _server = new();
    private readonly ServiceProvider _services;

    public ThunderstoreSourceTests()
    {
        _server.ServeRecordings();
        _services = _server.BuildServices();
    }

    /// <summary>
    /// The recorded community, as its listing index was asked for.
    /// </summary>
    internal static string Community { get; } = ThunderstoreServer.Recording("listing-index")["request"]!["path"]!.GetValue<string>().Split('/')[2];

    /// <summary>
    /// The recorded package's team and name, as Thunderstore described it.
    /// </summary>
    internal static (string Team, string Name) Package { get; } = (
        ThunderstoreServer.Body("package")["namespace"]!.GetValue<string>(),
        ThunderstoreServer.Body("package")["name"]!.GetValue<string>());

    /// <summary>
    /// The recorded package's versions, newest first, as its community's listing gives them.
    /// </summary>
    internal static IReadOnlyList<string> Versions { get; } =
    [
        .. ThunderstoreServer.Body("listing-chunk-1").AsArray()
            .Single(package => package!["full_name"]!.GetValue<string>() == $"{Package.Team}-{Package.Name}")!["versions"]!.AsArray()
            .Select(version => version!["version_number"]!.GetValue<string>()),
    ];

    protected override SourceId ExpectedId { get; } = new("thunderstore");

    protected override IReadOnlyList<Uri> OwnLinks { get; } =
    [
        new($"https://thunderstore.io/c/{Community}/p/{Package.Team}/{Package.Name}/"),
        new($"https://thunderstore.io/c/{Community}/p/{Package.Team}/{Package.Name}/v/{Versions[^1]}/"),
        new($"https://thunderstore.io/package/download/{Package.Team}/{Package.Name}/{Versions[^1]}/"),
        new($"ror2mm://v1/install/thunderstore.io/{Package.Team}/{Package.Name}/{Versions[0]}/"),
    ];

    protected override TimeProvider Clock => _server.Clock;

    protected override GameRef? SearchableGame { get; } = new(new SourceId("thunderstore"), Community);

    protected override bool HandsOverLinks => true;

    public void Dispose()
    {
        _services.Dispose();
        _server.Dispose();
    }

    protected override ThunderstoreSource CreateSource() => ThunderstoreServer.SourceOf(_services);
}
