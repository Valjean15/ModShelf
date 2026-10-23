using System.Collections.Concurrent;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Answers linking questions the way the test says, and records what it was asked and shown.
/// </summary>
internal sealed class ScriptedPrompt : IAuthPrompt
{
    public string ApiKey { get; set; } = FakeStrategy.GoodApiKey;

    public string Email { get; set; } = "jean@example.com";

    public string EmailCode { get; set; } = FakeStrategy.GoodEmailCode;

    public ConcurrentQueue<string> Asked { get; } = new();

    public Uri? KeyPage { get; private set; }

    public string? SentTo { get; private set; }

    public (Uri Page, bool Opened)? BrowserStep { get; private set; }

    public Task<string> AskEmailAsync(SourceId source, CancellationToken cancellationToken)
    {
        Asked.Enqueue("email");
        return Task.FromResult(Email);
    }

    public Task<string> AskApiKeyAsync(SourceId source, Uri? keyPage, CancellationToken cancellationToken)
    {
        Asked.Enqueue("api key");
        KeyPage = keyPage;
        return Task.FromResult(ApiKey);
    }

    public Task<string> AskEmailCodeAsync(SourceId source, string? sentTo, CancellationToken cancellationToken)
    {
        Asked.Enqueue("email code");
        SentTo = sentTo;
        return Task.FromResult(EmailCode);
    }

    public void ShowBrowserStep(SourceId source, Uri authorizeUrl, bool browserOpened)
    {
        Asked.Enqueue("browser");
        BrowserStep = (authorizeUrl, browserOpened);
    }
}
