using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;
using Microsoft.Win32;

using ModShelf.Application.Notifications.Ports;

namespace ModShelf.Platform.Notifications;

/// <summary>
/// Shows notices as Windows toasts. Windows groups toasts by app ID and names them after it; an app without an installer
/// has no Start-menu shortcut to carry one, so the ID is registered for the current user, under
/// <c>HKEY_CURRENT_USER\Software\Classes\AppUserModelId</c>, before the first toast.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ToastNotificationService : INotificationService
{
    /// <summary>
    /// ModShelf's app ID. Settings > System > Notifications lists ModShelf under it once a toast has shown.
    /// </summary>
    public const string DefaultApplicationId = "ModShelf.App";

    private const string AppIdsKey = @"Software\Classes\AppUserModelId";
    private const string DisplayNameValue = "DisplayName";

    private readonly string _applicationId;
    private readonly ILogger _logger;
    private readonly Lock _registering = new();
    private bool _registered;

    public ToastNotificationService(ILogger logger)
        : this(DefaultApplicationId, logger)
    {
    }

    /// <param name="applicationId">The app ID the toasts go by; tests use one of their own.</param>
    public ToastNotificationService(string applicationId, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentNullException.ThrowIfNull(logger);

        _applicationId = applicationId;
        _logger = logger;
    }

    /// <summary>
    /// The registry key, under HKEY_CURRENT_USER, that registers the app ID.
    /// </summary>
    public string RegistryKey => $@"{AppIdsKey}\{_applicationId}";

    public Task ShowAsync(Notification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // The Windows Runtime calls block, and want a thread they can join to the multithreaded apartment.
        return Task.Run(
            () =>
            {
                try
                {
                    Register();
                    ToastInterop.Show(_applicationId, ToastContent.For(notification));
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
