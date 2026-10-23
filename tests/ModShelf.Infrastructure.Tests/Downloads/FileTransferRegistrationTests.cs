using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads.Ports;
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

    [Fact]
    public async Task NoLogAnywhereContainsTheUrlsPathOrQuery()
    {
        _server
            .Respond(Responses.Status(HttpStatusCode.ServiceUnavailable))
            .Respond(Responses.Whole(File1000, stopAfter: 400, ending: StreamEnding.Drop))
            .Respond(Responses.Rest(File1000, from: 400));
        RecordingLoggerProvider logs = new();

        TransferOutcome outcome = await DownloadAsync(
            maxRetryAttempts: 2,
            url: "https://cdn.example.com/signed-a1b2c3/SkyUI_5_2_SE.7z?expires=1&signature=s3cr3t",
            logs: logs);

        Assert.Equal(TransferOutcome.Completed, outcome);
        Assert.Contains(logs.Entries, entry => entry.Contains("cdn.example.com", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("signed-a1b2c3", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("s3cr3t", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("expires=", StringComparison.Ordinal));
    }

    private async Task<TransferOutcome> DownloadAsync(
        int maxRetryAttempts,
        string url = "https://cdn.example.com/files/SkyUI_5_2_SE.7z",
        RecordingLoggerProvider? logs = null)
    {
        ServiceCollection services = new();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            if (logs is not null)
            {
                logging.AddProvider(logs);
            }
        });
        services.AddHttpFileTransfer(new FileTransferOptions { MaxRetryAttempts = maxRetryAttempts, RetryDelay = TimeSpan.Zero });
        services.AddHttpClient(HttpFileTransfer.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _server);

        await using ServiceProvider provider = services.BuildServiceProvider();
        IFileTransfer transfer = provider.GetRequiredService<IFileTransfer>();

        return await transfer.DownloadAsync(
            new DownloadTicket(new Uri(url)),
            Path.Combine(_folder.Path, "SkyUI_5_2_SE.7z"),
            new RecordingProgress(),
            TestContext.Current.CancellationToken);
    }
}
