using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Infrastructure;
using ModShelf.Infrastructure.Sources;
using ModShelf.Sources.Abstractions;
using ModShelf.Testing;

using WireMock;
using WireMock.Logging;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Types;
using WireMock.Util;

namespace ModShelf.Sources.CurseForge.Tests.Common;

/// <summary>
/// A stand-in for CurseForge's Core API: the answers Fixtures/Record-CurseForgeFixtures.ps1 recorded, and ones a test
/// writes, each given only to a request with an API key, as CurseForge gives them (ADR 0107).
/// </summary>
internal sealed class CurseForgeServer : IDisposable
{
    public const string KeyHeader = "x-api-key";

    private static readonly string RecordedFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Recorded");

    // The recordings of mods other than those a search found.
    private static readonly string[] OtherMods = ["slug", "mods", "mod"];

    // Every mod recorded, once, those searched for first, in the order CurseForge found them.
    private static readonly Lazy<JsonNode[]> RecordedMods = new(() =>
    [
        .. Directory.GetFiles(RecordedFolder, "search-*.json").Order(StringComparer.Ordinal)
            .Concat(OtherMods.Select(name => Path.Combine(RecordedFolder, $"{name}.json")))
            .Where(File.Exists)
            .SelectMany(file => ModsIn(Body(Path.GetFileNameWithoutExtension(file))["data"]))
            .DistinctBy(mod => mod["id"]!.GetValue<long>()),
    ]);

    private readonly WireMockServer _server = WireMockServer.Start();

    public Uri ApiAddress => new($"{_server.Url}/");

    public RecordingLoggerProvider Logs { get; } = new();

    public IReadOnlyList<ILogEntry> Requests => [.. _server.LogEntries];

    /// <summary>
    /// Each request's path and query, as asked, oldest first.
    /// </summary>
    public IReadOnlyList<string> Asked => [.. Requests.Select(entry => $"{entry.RequestMessage!.Method} {entry.RequestMessage.Path}{Query(entry)}")];

    public static bool HasRecordings => Directory.Exists(RecordedFolder) && Directory.GetFiles(RecordedFolder, "*.json").Length > 0;

    public static JsonNode Recording(string name)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RecordedFolder, $"{name}.json")))
            ?? throw new InvalidDataException($"The recording {name} is empty.");

    public static JsonNode Body(string name) => Recording(name)["response"]!["body"]!;

    /// <summary>
    /// The source from <paramref name="services"/>.
    /// </summary>
    public static CurseForgeSource SourceOf(IServiceProvider services) => services.GetServices<IModSource>().OfType<CurseForgeSource>().Single();

    /// <summary>
    /// Serves every recording, and every other search of the mods recorded (<see cref="ServeSearches"/>).
    /// </summary>
    public void ServeRecordings()
    {
        foreach (string file in Directory.GetFiles(RecordedFolder, "*.json"))
        {
            Serve(Path.GetFileNameWithoutExtension(file));
        }

        ServeSearches();
    }

    /// <summary>
    /// Answers a search no recording answers as CurseForge would, but over the mods recorded only: those of the game and
    /// class asked for, with the slug asked for, or whose names hold every word asked for; in the order asked for, its
    /// popularity being the order recorded; a page of them.
    /// </summary>
    public void ServeSearches()
        => _server.Given(Request.Create().UsingGet().WithPath("/v1/mods/search").WithHeader(KeyHeader, "*")).AtPriority(3)
            .RespondWith(Response.Create().WithCallback(request => Searched(request.Query)));

    /// <summary>
    /// The key a recorded request was sent with in place of the linked one: <see langword="null"/> for none.
    /// </summary>
    public static string? KeyOf(string name)
        => SentInsteadOfKey(Recording(name)["request"]!, out string? key) ? key : throw new InvalidOperationException($"The recording {name} was sent with the linked key.");

    /// <summary>
    /// Serves one recording to the request it recorded: its method, path, query and body, and its key: any, but for a
    /// request recorded without one, or with a made-up one.
    /// </summary>
    public void Serve(string name)
    {
        JsonNode recording = Recording(name);
        JsonNode request = recording["request"]!;
        JsonNode response = recording["response"]!;
        string method = request["method"]!.GetValue<string>();

        IRequestBuilder match = Request.Create().UsingMethod(method).WithPath(request["path"]!.GetValue<string>());
        foreach (string pair in request["query"]!.GetValue<string>().Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            match = match.WithParam(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts.Length > 1 ? parts[1] : string.Empty));
        }

        if (request["body"] is { } body)
        {
            match = match.WithBody(new JsonMatcher(body.ToJsonString()));
        }

        bool instead = SentInsteadOfKey(request, out string? key);
        match = !instead ? match.WithHeader(KeyHeader, "*")
            : key is null ? match.WithHeader(KeyHeader, "*", MatchBehaviour.RejectOnMatch)
            : match.WithHeader(KeyHeader, new ExactMatcher(key));
        _server.Given(match).AtPriority(instead ? 1 : 2).RespondWith(Answer(response["status"]!.GetValue<int>(), response["body"]));
    }

    /// <summary>
    /// From now on, answers <paramref name="method"/> for <paramref name="path"/>, with each of <paramref name="query"/>,
    /// with <paramref name="status"/> and <paramref name="body"/> wrapped as CurseForge wraps its answers, when the
    /// request has a key; before any recording.
    /// </summary>
    public void Answer(string method, string path, HttpStatusCode status, JsonNode? body, params (string Name, string Value)[] query)
    {
        IRequestBuilder match = Request.Create().UsingMethod(method).WithPath(path).WithHeader(KeyHeader, "*");
        foreach ((string name, string value) in query)
        {
            match = match.WithParam(name, value);
        }

        _server.Given(match).AtPriority(0).RespondWith(Answer((int)status, body));
    }

    /// <summary>
    /// Answers 403, with nothing in it, to every request without a key, as CurseForge does.
    /// </summary>
    public void RefuseWithoutKey()
        => _server.Given(Request.Create().WithPath("/*").WithHeader(KeyHeader, "*", MatchBehaviour.RejectOnMatch)).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.Forbidden));

    /// <summary>
    /// The source set up as the hosts set it up, with <paramref name="key"/> linked, or none.
    /// </summary>
    public ServiceProvider BuildServices(string? key = LinkedKey.Key, Func<CurseForgeOptions, CurseForgeOptions>? configure = null)
    {
        CurseForgeOptions options = new() { ApiAddress = ApiAddress };
        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddSingleton<ICredentialProvider>(new LinkedKey(key));
        services.AddSourceHttpClients(new SourceHttpOptions
        {
            MaxRetryAttempts = 0,
            RetryDelay = TimeSpan.Zero,
            AttemptTimeout = TimeSpan.FromSeconds(30),
        });
        services.AddCurseForgeSource(configure?.Invoke(options) ?? options);
        return services.BuildServiceProvider();
    }

    public void Dispose() => _server.Dispose();

    private static IResponseBuilder Answer(int status, JsonNode? body)
    {
        string text = body is JsonValue plain && plain.TryGetValue(out string? raw) ? raw : body?.ToJsonString() ?? string.Empty;
        IResponseBuilder answer = Response.Create().WithStatusCode(status);
        return text.Length == 0 ? answer : answer.WithHeader("Content-Type", "application/json; charset=utf-8").WithBody(text);
    }

    // The script writes a "key" only for a request sent without the linked one: null for none, or the made-up key.
    private static bool SentInsteadOfKey(JsonNode request, out string? key)
    {
        bool instead = request.AsObject().TryGetPropertyValue("key", out JsonNode? sent);
        key = sent?.GetValue<string>();
        return instead;
    }

    private static ResponseMessage Searched(IDictionary<string, WireMockList<string>>? query)
    {
        string? Asked(string name) => query is not null && query.TryGetValue(name, out WireMockList<string>? values) ? values.FirstOrDefault() : null;

        IEnumerable<JsonNode> mods = RecordedMods.Value.Where(mod => Number(mod["gameId"]) == Asked("gameId"));
        if (Asked("classId") is { } type)
        {
            mods = mods.Where(mod => Number(mod["classId"]) == type);
        }

        if (Asked("slug") is { } slug)
        {
            mods = mods.Where(mod => string.Equals(mod["slug"]?.GetValue<string>(), slug, StringComparison.OrdinalIgnoreCase));
        }

        if (Asked("searchFilter") is { } words)
        {
            mods = mods.Where(mod => words.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(word => mod["name"]!.GetValue<string>().Contains(word, StringComparison.OrdinalIgnoreCase)));
        }

        Func<JsonNode, IComparable?>? column = Asked("sortField") switch
        {
            "3" => mod => Date(mod["dateModified"]),
            "4" => mod => mod["name"]?.GetValue<string>().Trim().ToUpperInvariant(),
            "6" => mod => mod["downloadCount"]?.GetValue<long>(),
            "11" => mod => Date(mod["dateReleased"]) ?? Date(mod["dateCreated"]),
            _ => null,
        };
        JsonNode[] ordered = column is null ? [.. mods]
            : Asked("sortOrder") == "desc" ? [.. mods.OrderByDescending(column)]
            : [.. mods.OrderBy(column)];
        int index = int.Parse(Asked("index") ?? "0", CultureInfo.InvariantCulture);
        int pageSize = int.Parse(Asked("pageSize") ?? "50", CultureInfo.InvariantCulture);
        JsonNode[] page = [.. ordered.Skip(index).Take(pageSize)];
        JsonObject body = new()
        {
            ["data"] = new JsonArray([.. page.Select(mod => mod.DeepClone())]),
            ["pagination"] = new JsonObject { ["index"] = index, ["pageSize"] = pageSize, ["resultCount"] = page.Length, ["totalCount"] = ordered.Length },
        };
        ResponseMessage answer = new()
        {
            StatusCode = 200,
            BodyData = new BodyData { BodyAsString = body.ToJsonString(), DetectedBodyType = BodyType.String, Encoding = Encoding.UTF8 },
        };
        answer.AddHeader("Content-Type", "application/json; charset=utf-8");
        return answer;
    }

    private static IEnumerable<JsonNode> ModsIn(JsonNode? data) => data switch
    {
        JsonArray mods => mods.OfType<JsonNode>(),
        JsonObject mod => [mod],
        _ => [],
    };

    private static string? Number(JsonNode? node) => node?.GetValue<long>().ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset? Date(JsonNode? node)
        => node?.GetValue<string>() is { } text ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture) : null;

    private static string Query(ILogEntry entry)
        => entry.RequestMessage!.Url is { } url && new Uri(url).Query is { Length: > 0 } query ? query : string.Empty;
}
