using System.Collections.Concurrent;
using System.Web;

using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A browser and a loopback redirect together: opening an authorization page "approves" it by sending the
/// redirect back with the page's state and <see cref="Code"/>, as the user clicking Approve would.
/// </summary>
internal sealed class FakeBrowser : IBrowserLauncher, IOAuthRedirectListener
{
    // The listening redirect's state, shared with the FakeRedirect handed to the service.
    private RedirectState? _redirect;

    public ConcurrentQueue<Uri> Opened { get; } = new();

    /// <summary>
    /// Whether the system manages to open the browser. When it doesn't, the user opens the page by hand,
    /// and the redirect still comes back.
    /// </summary>
    public bool CanOpen { get; set; } = true;

    /// <summary>
    /// Whether the user approves. When false, no redirect ever comes.
    /// </summary>
    public bool UserApproves { get; set; } = true;

    /// <summary>
    /// The code the redirect carries.
    /// </summary>
    public string Code { get; set; } = FakeStrategy.GoodOAuthCode;

    public bool RedirectDisposed => _redirect?.Disposed ?? false;

    public Task<IOAuthRedirect> StartAsync(CancellationToken cancellationToken)
    {
        _redirect = new RedirectState();
        return Task.FromResult<IOAuthRedirect>(new FakeRedirect(_redirect));
    }

    public Task<bool> TryOpenAsync(Uri url, CancellationToken cancellationToken)
    {
        Opened.Enqueue(url);
        if (UserApproves && _redirect is not null)
        {
            string state = HttpUtility.ParseQueryString(url.Query)["state"]!;
            _redirect.Return(new Uri($"{_redirect.RedirectUri}?code={Code}&state={state}"));
        }

        return Task.FromResult(CanOpen);
    }

    private sealed class RedirectState
    {
        private readonly TaskCompletionSource<Uri> _returned = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Uri RedirectUri { get; } = new("http://127.0.0.1:49152/");

        public Task<Uri> Returned => _returned.Task;

        public bool Disposed { get; set; }

        public void Return(Uri uri) => _returned.TrySetResult(uri);
    }

    private sealed class FakeRedirect(RedirectState state) : IOAuthRedirect
    {
        public Uri RedirectUri => state.RedirectUri;

        public Task<Uri> WaitForRedirectAsync(CancellationToken cancellationToken) => state.Returned.WaitAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            state.Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
