using System.Runtime.Versioning;

using ModShelf.Application.Links;
using ModShelf.Application.Links.Ports;

namespace ModShelf.Platform.Links;

/// <summary>
/// Link schemes on macOS, in Launch Services: the app bundle that opens a scheme's links. ModShelf can only be that app
/// when it runs from its own bundle, which declares the scheme. The bundle ID it replaces is kept in ModShelf's
/// preferences, and put back when ModShelf gives the links back.
/// </summary>
/// <remarks>
/// Launch Services can't be told that no app opens a scheme. When none did before ModShelf, giving them back leaves
/// them with ModShelf, the one app that declares the scheme.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed class LaunchServicesSchemeRegistration : ISchemeRegistration
{
    private readonly Lazy<string?> _bundleId = new(LaunchServicesInterop.MainBundleId);

    public Task<SchemeState> GetAsync(string scheme, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        string? handler = LaunchServicesInterop.DefaultHandler(scheme);
        SchemeState state = IsOurs(handler)
            ? new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: LaunchServicesInterop.GetPreference(_bundleId.Value!, PreviousKey(scheme)))
            : _bundleId.Value is null ? SchemeState.Unavailable
            : handler is null ? SchemeState.None
            : new SchemeState(SchemeHandlerKind.OtherApp, handler);
        return Task.FromResult(state);
    }

    public Task RegisterAsync(string scheme, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        string bundleId = _bundleId.Value
            ?? throw new InvalidOperationException("ModShelf can open links on macOS only when it runs as an installed app.");

        string? handler = LaunchServicesInterop.DefaultHandler(scheme);
        if (!IsOurs(handler))
        {
            LaunchServicesInterop.SetPreference(bundleId, PreviousKey(scheme), handler);
        }

        LaunchServicesInterop.SetDefaultHandler(scheme, bundleId);
        return Task.CompletedTask;
    }

    public Task UnregisterAsync(string scheme, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        if (_bundleId.Value is not { } bundleId)
        {
            return Task.CompletedTask;
        }

        if (IsOurs(LaunchServicesInterop.DefaultHandler(scheme))
            && LaunchServicesInterop.GetPreference(bundleId, PreviousKey(scheme)) is { } previous)
        {
            LaunchServicesInterop.SetDefaultHandler(scheme, previous);
        }

        LaunchServicesInterop.SetPreference(bundleId, PreviousKey(scheme), null);
        return Task.CompletedTask;
    }

    private static string PreviousKey(string scheme) => $"PreviousHandler.{scheme}";

    // Launch Services compares bundle IDs ignoring case.
    private bool IsOurs(string? handler) => handler is not null && string.Equals(handler, _bundleId.Value, StringComparison.OrdinalIgnoreCase);
}
