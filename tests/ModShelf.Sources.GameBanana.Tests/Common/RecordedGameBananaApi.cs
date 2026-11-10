using System.Net;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Infrastructure;
using ModShelf.Infrastructure.Sources;
using ModShelf.Testing;

using WireMock.Logging;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace ModShelf.Sources.GameBanana.Tests.Common;

/// <summary>
/// GameBanana as recorded by <c>Fixtures/Record-GameBananaFixtures.ps1</c>, served by WireMock.Net on this computer, and
/// the source's services set up to call it.
/// </summary>
internal sealed class RecordedGameBananaApi : IDisposable
{
    /// <summary>
    /// The recorded game, Persona 5 Royal (PC).
    /// </summary>
    public const string Game = "16951";

    /// <summary>
    /// The recorded mod, P5R Female Protagonist: six files, one archived, and 44 updates.
    /// </summary>
    public const string Mod = "440088";

    /// <summary>
    /// Its main archive, version 1.2.28.
    /// </summary>
    public const string MainFile = "1722096";

    /// <summary>
    /// Its archived 1.2.27.
    /// </summary>
    public const string ArchivedFile = "1662070";

    private static readonly string RecordedFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Recorded");

    // What a request may ask that a recording without it doesn't answer: a filter, or a search of names only. WireMock
    // matches a query's names as sent, escaped.
    private static readonly string[] Optional =
    [
        .. new[] { "_aFilters[Generic_Category]", "_aFilters[Generic_Submitter]", "_aFilters[Generic_ContentRatings]", "_csvFields" }
            .Select(Uri.EscapeDataString),
    ];

    private readonly WireMockServer _server = WireMockServer.Start();

    /// <summary>
    /// Where GameBanana answers, ending in a slash.
    /// </summary>
    public Uri BaseAddress => new($"{_server.Url}/");

    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>
    /// Every request GameBanana got, oldest first.
    /// </summary>
    public IReadOnlyList<ILogEntry> Requests => [.. _server.LogEntries];

    /// <summary>
    /// Every recording, by its name, such as <c>mod</c> or <c>games-last</c>.
    /// </summary>
    public static IReadOnlyList<string> Recordings
        => [.. Directory.GetFiles(RecordedFolder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)];

    /// <summary>
    /// A recording: what was asked, and what GameBanana answered.
    /// </summary>
    public static JsonNode Recording(string name)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RecordedFolder, $"{name}.json")))
            ?? throw new InvalidDataException($"The recording {name} is empty.");

    /// <summary>
    /// The body GameBanana answered a recording with.
    /// </summary>
    public static JsonNode Body(string name) => Recording(name)["response"]!["body"]!;

    /// <summary>
    /// From now on, GameBanana answers the recorded request with the recorded answer; a request that asks something else,
    /// in its path, query, or a filter the recording didn't ask, isn't answered.
    /// </summary>
    public void Serve(string name)
    {
        JsonNode recording = Recording(name);
        JsonNode request = recording["request"]!;
        JsonNode response = recording["response"]!;

        IRequestBuilder match = Request.Create().UsingMethod(request["method"]!.GetValue<string>()).WithPath(request["path"]!.GetValue<string>());
        HashSet<string> asked = [];
        foreach (string pair in (request["query"]?.GetValue<string>() ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            asked.Add(parts[0]);
            match = match.WithParam(parts[0], Uri.UnescapeDataString(parts[1]));
        }

        foreach (string key in Optional.Where(key => !asked.Contains(key)))
        {
            match = match.WithParam(key, MatchBehaviour.RejectOnMatch);
        }

        IResponseBuilder answer = Response.Create().WithStatusCode(response["status"]!.GetValue<int>());
        foreach ((string header, JsonNode? value) in response["headers"]!.AsObject())
        {
            answer = answer.WithHeader(header, value!.GetValue<string>());
        }

        JsonNode? body = response["body"];
        answer = answer.WithBody(body is JsonValue text && text.TryGetValue(out string? plain) ? plain : body?.ToJsonString() ?? string.Empty);
        _server.Given(match).AtPriority(1).RespondWith(answer);
    }

    /// <summary>
    /// The whole recorded API: every recording but those that answer a request another answers too, as a game or mod
    /// that doesn't exist does once GameBanana keeps the answer; and the games list's pages that weren't recorded, as
    /// pages holding no games.
    /// </summary>
    public void ServeAll()
    {
        foreach (string name in Recordings.Where(name => !name.EndsWith("-kept", StringComparison.Ordinal)))
        {
            Serve(name);
        }

        int total = Body("games-1")["_aMetadata"]!["_nRecordCount"]!.GetValue<int>();
        _server
            .Given(Request.Create().UsingGet().WithPath("/apiv11/Game/Index"))
            .AtPriority(10)
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json; charset=UTF-8")
                .WithBody(new JsonObject
                {
                    ["_aMetadata"] = new JsonObject { ["_nRecordCount"] = total, ["_bIsComplete"] = false, ["_nPerpage"] = 50 },
                    ["_aRecords"] = new JsonArray(),
                }.ToJsonString()));
    }

    /// <summary>
    /// From now on, GameBanana answers GET requests for <paramref name="path"/> as the test sets up.
    /// </summary>
    public void Answer(string path, Func<IResponseBuilder, IResponseBuilder> response)
        => _server.Given(Request.Create().UsingGet().WithPath(path)).AtPriority(0).RespondWith(response(Response.Create()));

    /// <summary>
    /// Forgets every answer set up so far, as when GameBanana comes back after it failed.
    /// </summary>
    public void Reset() => _server.ResetMappings();

    /// <summary>
    /// Answers requests to <paramref name="path"/> with <paramref name="parameter"/> set to <paramref name="value"/> as
    /// the test says, before any recording.
    /// </summary>
    public void Answer(string path, string parameter, string value, Func<IResponseBuilder, IResponseBuilder> response)
        => _server.Given(Request.Create().UsingGet().WithPath(path).WithParam(parameter, value)).AtPriority(0).RespondWith(response(Response.Create()));

    /// <summary>
    /// The source's services, as the hosts register them, calling this GameBanana: <c>AddSourceHttpClients()</c>, with no
    /// account linked, then <c>AddGameBananaSource()</c>. Retries don't wait.
    /// </summary>
    public ServiceProvider BuildServices(Func<GameBananaOptions, GameBananaOptions>? configure = null, int retries = 3)
    {
        GameBananaOptions options = new() { BaseAddress = BaseAddress };
        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddSingleton<ICredentialProvider, NoLinkedAccount>();
        services.AddSourceHttpClients(new SourceHttpOptions
        {
            MaxRetryAttempts = retries,
            RetryDelay = TimeSpan.Zero,
            AttemptTimeout = TimeSpan.FromSeconds(10),
        });
        services.AddGameBananaSource(configure?.Invoke(options) ?? options);
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        Logs.Dispose();
    }
}
