using System.Collections.Concurrent;
using System.Globalization;
using System.Web;

using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// An authentication strategy that behaves like a small site: it accepts one API key, one emailed code, or an
/// OAuth redirect carrying the code and the state it issued. Refreshing does whatever the test scripts.
/// </summary>
internal sealed class FakeStrategy : IAuthStrategy
{
    public const string GoodApiKey = "good-key";
    public const string GoodEmailCode = "123456";
    public const string GoodOAuthCode = "c0de";
    public const string ApiKeyHeader = "X-Api-Key";

    private int _refreshCalls;
    private int _issued;

    private FakeStrategy(AuthKind kind, TimeProvider timeProvider)
    {
        Kind = kind;
        TimeProvider = timeProvider;
    }

    public AuthKind Kind { get; }

    public TimeProvider TimeProvider { get; }

    public ConcurrentQueue<AuthStart> Starts { get; } = new();

    public ConcurrentQueue<string> Answers { get; } = new();

    public int RefreshCalls => Volatile.Read(ref _refreshCalls);

    /// <summary>
    /// What a refresh returns. By default a new token valid for an hour, or for an API key nothing, as keys don't refresh.
    /// </summary>
    public Func<AccountCredentials, Task<AccountCredentials?>> Refresh { get; set; } = null!;

    /// <summary>
    /// Runs while <see cref="CompleteAsync"/> checks the answer with the site, before it answers.
    /// </summary>
    public Func<Task>? WhileCompleting { get; set; }

    public static FakeStrategy ApiKey(TimeProvider timeProvider)
        => new(AuthKind.ApiKey, timeProvider) { Refresh = _ => Task.FromResult<AccountCredentials?>(null) };

    public static FakeStrategy EmailCode(TimeProvider timeProvider)
    {
        FakeStrategy strategy = new(AuthKind.EmailCode, timeProvider);
        strategy.Refresh = _ => Task.FromResult<AccountCredentials?>(null);
        return strategy;
    }

    public static FakeStrategy OAuth(TimeProvider timeProvider)
    {
        FakeStrategy strategy = new(AuthKind.OAuthPkce, timeProvider);
        strategy.Refresh = _ => Task.FromResult<AccountCredentials?>(strategy.NewTokens());
        return strategy;
    }

    /// <summary>
    /// Tokens as the site issues them: numbered, valid for an hour from now.
    /// </summary>
    public AccountCredentials NewTokens()
    {
        int number = Interlocked.Increment(ref _issued);
        return new AccountCredentials(
            string.Create(CultureInfo.InvariantCulture, $"access-{number}"),
            string.Create(CultureInfo.InvariantCulture, $"refresh-{number}"),
            TimeProvider.GetUtcNow().AddHours(1));
    }

    public Task<AuthChallenge> BeginAsync(AuthStart start, CancellationToken cancellationToken)
    {
        Starts.Enqueue(start);
        return Task.FromResult(Kind switch
        {
            AuthKind.ApiKey => new AuthChallenge(AuthKind.ApiKey, new Uri("https://example.com/account/api-keys")),
            AuthKind.EmailCode => new AuthChallenge(AuthKind.EmailCode, sentTo: "j***@example.com"),
            _ => new AuthChallenge(
                AuthKind.OAuthPkce,
                new Uri($"https://auth.example.com/authorize?redirect_uri={Uri.EscapeDataString(start.RedirectUri!.AbsoluteUri)}&state=st4te-0f9e"),
                state: new Dictionary<string, string> { ["state"] = "st4te-0f9e", ["code_verifier"] = "v3rifier" }),
        });
    }

    public async Task<AccountCredentials> CompleteAsync(AuthChallenge challenge, string answer, CancellationToken cancellationToken)
    {
        Answers.Enqueue(answer);
        if (WhileCompleting is not null)
        {
            await WhileCompleting();
        }

        bool accepted = Kind switch
        {
            AuthKind.ApiKey => answer == GoodApiKey,
            AuthKind.EmailCode => answer == GoodEmailCode,
            _ => HttpUtility.ParseQueryString(new Uri(answer).Query) is var query
                && query["state"] == challenge.State["state"]
                && query["code"] == GoodOAuthCode,
        };
        if (!accepted)
        {
            throw new AuthenticationFailedException("The site refused that.");
        }

        return Kind == AuthKind.OAuthPkce ? NewTokens() : new AccountCredentials($"token-for-{answer}");
    }

    /// <summary>
    /// Sends an API key in <see cref="ApiKeyHeader"/>, and any other credentials as a bearer token, as sites do.
    /// </summary>
    public void Authorize(HttpRequestMessage request, AccountCredentials credentials)
    {
        if (Kind == AuthKind.ApiKey)
        {
            request.Headers.Add(ApiKeyHeader, credentials.AccessToken);
        }
        else
        {
            request.Headers.Authorization = new("Bearer", credentials.AccessToken);
        }
    }

    public Task<AccountCredentials?> RefreshAsync(AccountCredentials expired, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _refreshCalls);
        return Refresh(expired);
    }
}
