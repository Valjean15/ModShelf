using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;
using Microsoft.Win32;

using ModShelf.Application.Notifications.Ports;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// Shows notices as Windows toasts. Windows groups toasts by app ID and names them after it; an app without an installer
/// has no Start-menu shortcut to carry one, so the ID is registered for the current user, under
/// <c>HKEY_CURRENT_USER\Software\Classes\AppUserModelId</c>, before the first toast. A click on a toast opens ModShelf
/// through a link of its own (<see cref="NoticeClicks"/>), which works with or without the installer's shortcut.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ToastNotificationService : INotificationService
{
    private const string DisplayNameValue = "DisplayName";

    private readonly string _applicationId;
    private readonly NoticeClicks? _clicks;
    private readonly ILogger _logger;
    private readonly Lock _registering = new();
    private bool _registered;

    /// <param name="clicks">What a click on a toast opens.</param>
    public ToastNotificationService(NoticeClicks clicks, ILogger logger)
        : this(ToastAppId.Default, clicks, logger)
    {
    }

    /// <param name="applicationId">The app ID the toasts go by; tests use one of their own.</param>
    public ToastNotificationService(string applicationId, ILogger logger)
        : this(applicationId, null, logger)
    {
    }

    /// <param name="applicationId">The app ID the toasts go by; tests use one of their own.</param>
    /// <param name="clicks">What a click on a toast opens; null for nothing, and a click only closes it.</param>
    public ToastNotificationService(string applicationId, NoticeClicks? clicks, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentNullException.ThrowIfNull(logger);

        _applicationId = applicationId;
        _clicks = clicks;
        _logger = logger;
    }

    /// <summary>
    /// The registry key, under HKEY_CURRENT_USER, that registers the app ID.
    /// </summary>
    public string RegistryKey => ToastAppId.KeyOf(_applicationId);

    public Task ShowAsync(Notification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // The Windows Runtime calls block, and want a thread they can join to the multithreaded apartment.
        return Task.Run(
            async () =>
            {
                try
                {
                    Register();
                    string? link = _clicks is null ? null : await _clicks.LinkAsync(cancellationToken).ConfigureAwait(false);
                    ToastInterop.Show(_applicationId, ToastContent.For(notification, link));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    NotificationServices.LogCantShow(_logger, exception, notification.Title);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Registers the app ID with ModShelf's name, once, unless it's registered so already.
    /// </summary>
    internal void Register()
    {
        lock (_registering)
        {
            if (_registered)
            {
                return;
            }

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKey);
            if (!string.Equals(key.GetValue(DisplayNameValue) as string, NotificationServices.AppName, StringComparison.Ordinal))
            {
                key.SetValue(DisplayNameValue, NotificationServices.AppName, RegistryValueKind.String);
            }

            _registered = true;
        }
    }
}
