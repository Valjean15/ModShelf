using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.ViewModels.Quota;

/// <summary>
/// The strip at the top of the window while a site's requests run low: calm while some are left, below the share
/// <see cref="AppSettings.QuotaWarnBelow"/> says of the hour's or the day's, warm once they're used up, and saying when
/// they renew. It looks every <see cref="CheckEvery"/>, as the sites say what's left with each answer. Dismissed, it
/// stays hidden for that site until its requests renew. The Accounts screen keeps the full numbers.
/// </summary>
public sealed partial class QuotaWarningViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How often the strip looks at what the sites have left: every 30 seconds.
    /// </summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(30);

    private readonly ISourceRegistry _sources;
    private readonly ISettingsService _settings;
    private readonly IUiThread _uiThread;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<QuotaWarningViewModel> _logger;
    private readonly Dictionary<SourceId, DateTimeOffset> _dismissed = [];
    private (SourceId Source, DateTimeOffset RenewsAt)? _shown;
    private ITimer? _timer;
    private bool _settingsFailed;

    public QuotaWarningViewModel(
        ISourceRegistry sources,
        ISettingsService settings,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ILogger<QuotaWarningViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _settings = settings;
        _uiThread = uiThread;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Whether the strip shows.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    /// <summary>
    /// Such as "Nexus Mods has 42 of its 500 requests left this hour. They renew at 4:00 PM."
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the requests are used up, which the strip shows warm rather than calm.
    /// </summary>
    [ObservableProperty]
    public partial bool IsUsedUp { get; private set; }

    /// <summary>
    /// The last look, for tests to wait on; it never throws.
    /// </summary>
    internal Task Checking { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Looks now, then every <see cref="CheckEvery"/>, on the UI thread.
    /// </summary>
    public void Start() => _timer ??= _timeProvider.CreateTimer(_ => _uiThread.Post(Check), null, TimeSpan.Zero, CheckEvery);

    public void Dispose() => _timer?.Dispose();

    /// <summary>
    /// Looks at what the sites have left, and shows the strip for the first that's low, or hides it.
    /// </summary>
    internal void Check() => Checking = CheckAsync();

    /// <summary>
    /// Hides the strip until the requests it's about renew.
    /// </summary>
    [RelayCommand]
    private void Dismiss()
    {
        if (_shown is { } shown)
        {
            _dismissed[shown.Source] = shown.RenewsAt;
        }

        _shown = null;
        IsShown = false;
    }

    private async Task CheckAsync()
    {
        int warnBelow;
        try
        {
            warnBelow = (await _settings.GetAsync(CancellationToken.None)).QuotaWarnBelow ?? AppSettings.DefaultQuotaWarnBelow;
            _settingsFailed = false;
        }
        catch (InvalidDataException exception)
        {
            // The Settings screen says why; the default holds meanwhile, logged once.
            if (!_settingsFailed)
            {
                LogSettingsUnread(_logger, exception);
            }

            _settingsFailed = true;
            warnBelow = AppSettings.DefaultQuotaWarnBelow;
        }

        Show(warnBelow);
    }

    private void Show(int warnBelow)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (warnBelow > 0)
        {
            foreach (IApiQuota source in _sources.All<IApiQuota>())
            {
                if (source.Quota is not { } quota
                    || (_dismissed.TryGetValue(source.Id, out DateTimeOffset until) && now < until)
                    || Allowances.Low(quota, now, warnBelow) is not { } allowance)
                {
                    continue;
                }

                string site = _sources.SiteName(source.Id);
                string renews = Allowances.RenewsAt(allowance, _timeProvider);
                IsUsedUp = allowance.Remaining == 0;
                Message = IsUsedUp
                    ? Text.Format(Strings.QuotaUsedUp, site, renews)
                    : Text.Format(
                        Allowances.Of(allowance, Strings.QuotaLowHour, Strings.QuotaLowDay, Strings.QuotaLow),
                        site,
                        allowance.Remaining,
                        allowance.Limit,
                        renews);
                _shown = (source.Id, allowance.ResetsAt);
                IsShown = true;
                return;
            }
        }

        _shown = null;
        IsShown = false;
    }
}
