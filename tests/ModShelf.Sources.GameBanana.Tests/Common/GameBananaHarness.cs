using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;

namespace ModShelf.Sources.GameBanana.Tests.Common;

/// <summary>
/// The source against the recorded GameBanana, set up as the hosts set it up, without retries, so each request is asked
/// once; with no account linked, or the member <c>linkedMember</c> linked by their profile.
/// </summary>
internal sealed class GameBananaHarness(string? linkedMember = null) : IDisposable
{
    public static readonly SourceId GameBanana = new("gamebanana");

    /// <summary>
    /// The recorded member, chlorophylls, who submitted the recorded mod.
    /// </summary>
    public const string Member = "1740038";

    private ServiceProvider? _services;

    /// <summary>
    /// Persona 5 Royal (PC).
    /// </summary>
    public static GameRef Game { get; } = new(GameBanana, RecordedGameBananaApi.Game);

    /// <summary>
    /// P5R Female Protagonist.
    /// </summary>
    public static ModRef Mod { get; } = new(Game, RecordedGameBananaApi.Mod);

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public RecordedGameBananaApi Api { get; } = new();

    public GameBananaSource Source
        => (_services ??= Api.BuildServices(retries: 0, linkedMember: linkedMember)).GetServices<IModSource>().OfType<GameBananaSource>().Single();

    /// <summary>
    /// What GameBanana was asked, each as its path and query, oldest first.
    /// </summary>
    public IReadOnlyList<string> Asked
        => [.. Api.Requests.Select(entry => entry.RequestMessage!.Path + entry.RequestMessage.RawQuery)];

    public void Dispose()
    {
        _services?.Dispose();
        Api.Dispose();
    }
}
