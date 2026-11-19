using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Thunderstore.Tests.Common;

/// <summary>
/// The source against a Thunderstore the test writes, set up as the hosts set it up, without retries, so each request is
/// asked once. Its one community is <see cref="Community"/>.
/// </summary>
internal sealed class ThunderstoreHarness : IDisposable
{
    public const string Community = "game";

    public static readonly SourceId Thunderstore = new("thunderstore");

    private ServiceProvider? _services;

    public static GameRef Game { get; } = new(Thunderstore, Community);

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ThunderstoreServer Server { get; } = new();

    public ThunderstoreSource Source => ThunderstoreServer.SourceOf(_services ??= Server.BuildServices());

    public static ModRef Mod(string fullName) => new(Game, fullName);

    public static ModFileRef File(string fullName, string version) => new(Mod(fullName), version);

    /// <summary>
    /// Serves the community's listing as one chunk of these packages.
    /// </summary>
    public void List(params TestPackage[] packages) => Server.ServeListing(Community, [TestPackage.Chunk(packages)]);

    public void Dispose()
    {
        _services?.Dispose();
        Server.Dispose();
    }
}
