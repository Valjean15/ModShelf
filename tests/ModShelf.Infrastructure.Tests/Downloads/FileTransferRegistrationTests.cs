using System.Net;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Downloads;
using ModShelf.Infrastructure.Downloads;
using ModShelf.Infrastructure.Tests.Fakes;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Infrastructure.Tests.Downloads;

/// <summary>
/// The transfer as the hosts register it, with the real HTTP client pipeline and its retries.
/// </summary>
public sealed class FileTransferRegistrationTests : IDisposable
{
    private static readonly byte[] File1000 = Responses.SampleFile(1000);

    private readonly TempFolder _folder = new();
    private readonly ScriptedHttpHandler _server = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task RetriesTransientServerErrors()
    {
        _server
            .Respond(Responses.Status(HttpStatusCode.ServiceUnavailable))
            .Respond(Responses.Status(HttpStatusCode.ServiceUnavailable))
            .Respond(Responses.Whole(File1000));

        TransferOutcome outcome = await DownloadAsync(maxRetryAttempts: 2);

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Equal(3, _server.Requests.Count);
    }

    [Fact]
    public async Task GivesUpOnServerErrorsOnceTheRetriesRunOut()
    {
        _server.RespondAlways(Responses.Status(HttpStatusCode.ServiceUnavailable));

        TransferOutcome outcome = await DownloadAsync(maxRetryAttempts: 2);

        Assert.Equal(TransferOutcome.NetworkError, outcome);
        Assert.Equal(3, _server.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryAnExpiredLink()
    {
        _server.RespondAlways(Responses.Status(HttpStatusCode.Forbidden));

        TransferOutcome outcome = await DownloadAsync(maxRetryAttempts: 2);

        Assert.Equal(TransferOutcome.LinkExpired, outcome);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task IdentifiesItselfAsModShelf()
    {
        _server.Respond(Responses.Whole(File1000));

        await DownloadAsync(maxRetryAttempts: 0);

        Assert.StartsWith("ModShelf/", Assert.Single(_server.Requests).UserAgent, StringComparison.Ordinal);
    }

    private async Task<TransferOutcome> DownloadAsync(int maxRetryAttempts)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddHttpFileTransfer(new FileTransferOptions { MaxRetryAttempts = maxRetryAttempts, RetryDelay = TimeSpan.Zero });
        services.AddHttpClient(HttpFileTransfer.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _server);

        await using ServiceProvider provider = services.BuildServiceProvider();
        IFileTransfer transfer = provider.GetRequiredService<IFileTransfer>();

        return await transfer.DownloadAsync(
            new DownloadTicket(new Uri("https://cdn.example.com/files/SkyUI_5_2_SE.7z")),
            Path.Combine(_folder.Path, "SkyUI_5_2_SE.7z"),
            new RecordingProgress(),
            TestContext.Current.CancellationToken);
    }
}
