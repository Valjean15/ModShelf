using System.Collections.Concurrent;

using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// The user's browser, scripted. Opening the authorization page loads it, and the site's redirect back to the app's
/// loopback address is followed, as a real browser does once the user approves. It can also fail to open, as on a
/// computer without a browser, or open the page and leave it, like a user who never approves.
/// </summary>
internal sealed class ScriptedBrowser : IBrowserLauncher, IDisposable
{
    private readonly HttpClient _browser = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ConcurrentQueue<Uri> _opened = new();
    private readonly ConcurrentQueue<Task<BrowserVisit>> _visits = new();

    /// <summary>
    /// Whether the system manages to open the browser.
    /// </summary>
    public bool Opens { get; set; } = true;

    /// <summary>
    /// Whether the user gets on with the page that opened. When false, the page just stays open.
    /// </summary>
    public bool UserActs { get; set; } = true;

    /// <summary>
    /// The pages the app opened, oldest first.
    /// </summary>
    public IReadOnlyList<Uri> Opened => [.. _opened];

    /// <summary>
    /// Each page loaded so far, with where its redirects ended.
    /// </summary>
    public IReadOnlyList<Task<BrowserVisit>> Visits => [.. _visits];

    public Task<bool> TryOpenAsync(Uri url, CancellationToken cancellationToken)
    {
        if (!Opens)
        {
            return Task.FromResult(false);
        }

        _opened.Enqueue(url);
        if (UserActs)
        {
            _ = VisitAsync(url);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Loads <paramref name="url"/> and follows its redirects, as when the user opens a page by hand.
    /// </summary>
    public Task<BrowserVisit> VisitAsync(Uri url)
    {
        Task<BrowserVisit> visit = LoadAsync(url);
        _visits.Enqueue(visit);
        return visit;
    }

    public void Dispose() => _browser.Dispose();

    private async Task<BrowserVisit> LoadAsync(Uri url)
    {
        using HttpResponseMessage page = await _browser.GetAsync(url).ConfigureAwait(false);
        return new BrowserVisit(
            page.RequestMessage?.RequestUri ?? url,
            page.StatusCode,
            await page.Content.ReadAsStringAsync().ConfigureAwait(false));
    }
}
