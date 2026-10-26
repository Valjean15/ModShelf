using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ModShelf.Testing;

using WireMock.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace ModShelf.Sources.Nexus.Tests.Common;

/// <summary>
/// The Nexus Mods API as recorded by <c>Fixtures/Record-NexusFixtures.ps1</c>, served by WireMock.Net on this computer,
/// and the source's services set up to call it.
/// </summary>
internal sealed class RecordedNexusApi : IDisposable
{
    public const string Premium = "premium";
    public const string Free = "free";

    private static readonly string RecordedFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Recorded");

    private readonly WireMockServer _server = WireMockServer.Start();

    /// <summary>
    /// Where the API answers, ending in a slash.
    /// </summary>
    public Uri BaseAddress => new($"{_server.Url}/");

    /// <summary>
    /// The clock the source's services use.
    /// </summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>
    /// Every request the API got, oldest first.
    /// </summary>
    public IReadOnlyList<ILogEntry> Requests => [.. _server.LogEntries];

    /// <summary>
    /// A recording: what was asked, and what the API answered.
    /// </summary>
    public static JsonNode Recording(string account, string name)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RecordedFolder, account, $"{name}.json")))
            ?? throw new InvalidDataException($"The recording {account}/{name} is empty.");

    /// <summary>
    /// From now on, the API answers the recorded request with the recorded response.
    /// </summary>
    public void Serve(string account, string name)
    {
        JsonNode recording = Recording(account, name);
        JsonNode request = recording["request"]!;
        JsonNode response = recording["response"]!;

        IRequestBuilder match = Request.Create().UsingGet().WithPath(request["path"]!.GetValue<string>());
        foreach (string pair in request["query"]!.GetValue<string>().Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            match = match.WithParam(parts[0], Uri.UnescapeDataString(parts[1]));
        }

        IResponseBuilder answer = Response.Create().WithStatusCode(response["status"]!.GetValue<int>());
        foreach ((string header, JsonNode? value) in response["headers"]!.AsObject())
        {
            answer = answer.WithHeader(header, value!.GetValue<string>());
        }

        JsonNode? body = response["body"];
        answer = answer.WithBody(body is JsonValue text && text.TryGetValue(out string? plain) ? plain : body?.ToJsonString() ?? string.Empty);
        _server.Given(match).RespondWith(answer);
    }

    /// <summary>
    /// From now on, the API answers GET requests for <paramref name="path"/> as the test sets up.
    /// </summary>
    public void Answer(string path, Func<IResponseBuilder, IResponseBuilder> response)
        => _server.Given(Request.Create().UsingGet().WithPath(path)).RespondWith(response(Response.Create()));

    /// <summary>
    /// From now on, POST requests to <paramref name="path"/> are answered as the test sets up.
    /// </summary>
    public void AnswerPost(string path, Func<IResponseBuilder, IResponseBuilder> response)
        => _server.Given(Request.Create().UsingPost().WithPath(path)).RespondWith(response(Response.Create()));

    /// <summary>
    /// Answers GET requests for <paramref name="path"/> with each response in turn, then the last one for good.
    /// </summary>
    public void AnswerInTurn(string path, params Func<IResponseBuilder, IResponseBuilder>[] responses)
    {
        string scenario = Guid.NewGuid().ToString("N");
        for (int index = 0; index < responses.Length; index++)
        {
            string state = $"answer-{index}";
            IRespondWithAProvider given = _server.Given(Request.Create().UsingGet().WithPath(path)).InScenario(scenario);
            given = index == 0 ? given : given.WhenStateIs(state);
            if (index < responses.Length - 1)
            {
                given = given.WillSetStateTo($"answer-{index + 1}");
            }

            given.RespondWith(responses[index](Response.Create()));
        }
    }

    /// <summary>
    /// The source's services, as <c>AddNexusSource()</c> registers them, calling this API. Retries don't wait.
    /// </summary>
    /// <param name="realClock">
    /// Whether they run on the system's clock rather than <see cref="Time"/>. Timeouts need it: the retry pipeline
    /// waits on the registered clock.
    /// </param>
    public ServiceProvider BuildServices(Func<NexusOptions, NexusOptions>? configure = null, bool realClock = false)
    {
        // The sign-in server answers here too, under /oauth/.
        NexusOptions options = new()
        {
            ApiBaseAddress = BaseAddress,
            UsersBaseAddress = BaseAddress,
            RetryDelay = TimeSpan.Zero,
            AttemptTimeout = TimeSpan.FromSeconds(10),
        };

        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddSingleton(realClock ? TimeProvider.System : Time);
        services.AddNexusSource(configure?.Invoke(options) ?? options);
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        Logs.Dispose();
    }
}
