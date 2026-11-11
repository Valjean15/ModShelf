using Microsoft.Extensions.DependencyInjection;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;

namespace ModShelf.Sources.ModIo.Tests.Common;

/// <summary>
/// The source against the recorded mod.io, set up as the hosts set it up, without retries, so each request is asked
/// once; with no account linked, or <c>linked</c>, an API key or an access token.
/// </summary>
internal sealed class ModIoHarness(string? linked = null, int requestsPerMinute = 10_000) : IDisposable
{
    public static readonly SourceId ModIo = new("modio");

    private ServiceProvider? _services;

    /// <summary>
    /// Baldur's Gate 3.
    /// </summary>
    public static GameRef Game { get; } = new(ModIo, RecordedModIoApi.Game);

    /// <summary>
    /// Better Inventory UI.
    /// </summary>
    public static ModRef Mod { get; } = new(Game, RecordedModIoApi.Mod);

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public RecordedModIoApi Api { get; } = new();

    public ServiceProvider Services => _services ??= Api.BuildServices(retries: 0, linked: linked, requestsPerMinute: requestsPerMinute);

    public ModIoSource Source => Services.GetServices<IModSource>().OfType<ModIoSource>().Single();

    /// <summary>
    /// The credentials linked, when they are.
    /// </summary>
    public LinkedCredentials? Linked => Services.GetService<LinkedCredentials>();

    /// <summary>
    /// What mod.io was asked, each as its path and query, without the API key, unescaped, oldest first.
    /// </summary>
    public IReadOnlyList<string> Asked
        => [.. Api.Requests.Select(entry => Uri.UnescapeDataString(entry.RequestMessage!.Path + WithoutKey(entry.RequestMessage.RawQuery)))];

    public void Dispose()
    {
        _services?.Dispose();
        Api.Dispose();
    }

    private static string WithoutKey(string? query)
    {
        string[] pairs = [.. (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Where(pair => !pair.StartsWith("api_key=", StringComparison.Ordinal))];
        return pairs.Length == 0 ? string.Empty : "?" + string.Join('&', pairs);
    }
}
