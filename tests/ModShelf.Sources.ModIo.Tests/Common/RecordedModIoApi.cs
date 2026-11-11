using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Accounts;
using ModShelf.Infrastructure;
using ModShelf.Infrastructure.Sources;
using ModShelf.Testing;

using WireMock;
using WireMock.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Types;
using WireMock.Util;

namespace ModShelf.Sources.ModIo.Tests.Common;

/// <summary>
/// mod.io as recorded by <c>Fixtures/Record-ModIoFixtures.ps1</c>, served by WireMock.Net on this computer, and the
/// source's services set up to call it, on a clock at the time of the recordings.
/// </summary>
/// <remarks>
/// <para>
/// A request is answered with the recording of the same path, query and platform, whatever credential it carries:
/// the <c>api_key</c> the strategy adds is left out of the match. Every host mod.io answered on, <c>api.mod.io</c> and
/// each game's and user's own, is this one, so the links to mod.io's API in what it answers are rewritten to here.
/// </para>
/// <para>
/// What wasn't recorded word for word, a game's mods narrowed or ordered another way, the games list's later pages, and
/// a game's file changes since another time, is answered from what was: the recorded mods narrowed and ordered as asked,
/// pages holding no games, and the recorded file changes since that time. Anything else is answered 501, "not recorded".
/// </para>
/// </remarks>
internal sealed partial class RecordedModIoApi : IDisposable
{
    /// <summary>
    /// The recorded game, Baldur's Gate 3: about 9,200 mods, files per platform.
    /// </summary>
    public const string Game = "6715";

    /// <summary>
    /// Its name in links.
    /// </summary>
    public const string GameName = "baldursgate3";

    /// <summary>
    /// The recorded mod, Better Inventory UI: ten files, one of them live on every platform.
    /// </summary>
    public const string Mod = "4228735";

    /// <summary>
    /// Its name in links.
    /// </summary>
    public const string ModName = "better-inventory-ui1";

    /// <summary>
    /// Its live file, 1.4.4.21.
    /// </summary>
    public const string LiveFile = "6795889";

    /// <summary>
    /// The version before it, 1.4.3.19.
    /// </summary>
    public const string PreviousFile = "6278773";

    /// <summary>
    /// A game closed to apps such as ModShelf.
    /// </summary>
    public const string ClosedGame = "304";

    /// <summary>
    /// An API key, as made up as the recordings' account: 32 hexadecimal characters.
    /// </summary>
    public const string ApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>
    /// When the file changes the recordings hold start: 28 days before they were recorded.
    /// </summary>
    public const long ChangesSince = 1791981313;

    /// <summary>
    /// An access token, as made up as the recordings' account: a JSON Web Token's three parts.
    /// </summary>
    public static readonly string AccessToken = $"eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.{new string('Q', 400)}.{new string('s', 300)}-_";

    private const string PlatformHeader = "X-Modio-Platform";

    private static readonly string RecordedFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Recorded");

    // The parameters the recorded mods can be narrowed and ordered by.
    private static readonly string[] ModFilters = ["_sort", "_limit", "_offset", "_q", "submitted_by", "submitted_by_display_name", "tags", "date_updated-min", "name_id"];

    // What a game's mods are ordered by, as mod.io names its columns.
    private static readonly Dictionary<string, Func<JsonNode, IComparable?>> Columns = new(StringComparer.Ordinal)
    {
        ["id"] = mod => Long(mod, "id"),
        ["name"] = mod => mod["name"]?.GetValue<string>(),
        ["date_live"] = mod => Long(mod, "date_live"),
        ["date_updated"] = mod => Long(mod, "date_updated"),
        ["date_added"] = mod => Long(mod, "date_added"),
        ["downloads_total"] = mod => Long(mod["stats"], "downloads_total"),
        ["downloads_today"] = mod => Long(mod["stats"], "downloads_today"),
    };

    private readonly WireMockServer _server = WireMockServer.Start();

    // Each request served, as Key writes it, and the recording that answers it.
    private readonly ConcurrentDictionary<string, string> _served = new(StringComparer.Ordinal);

    public RecordedModIoApi()
    {
        _server.Given(Request.Create().UsingAnyMethod()).AtPriority(100).RespondWith(Response.Create().WithCallback(Answer));
    }

    /// <summary>
    /// Where mod.io answers, ending in a slash.
    /// </summary>
    public Uri BaseAddress => new($"{_server.Url}/");

    /// <summary>
    /// mod.io's API here, as every host answered on.
    /// </summary>
    public Uri ApiAddress => new(BaseAddress, "v1/");

    /// <summary>
    /// The time the recordings were made at, which the services' clock starts at.
    /// </summary>
    public static DateTimeOffset RecordedAt { get; } = DateTimeOffset.FromUnixTimeSeconds(ChangesSince) + TimeSpan.FromDays(28);

    /// <summary>
    /// The services' clock.
    /// </summary>
    public FakeTimeProvider Clock { get; } = new(RecordedAt);

    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>
    /// Every request mod.io got, oldest first.
    /// </summary>
    public IReadOnlyList<ILogEntry> Requests => [.. _server.LogEntries];

    /// <summary>
    /// Every recording, by its name, such as <c>mod</c> or <c>mods-newest-1</c>.
    /// </summary>
    public static IReadOnlyList<string> Recordings
        => [.. Directory.GetFiles(RecordedFolder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)];

    /// <summary>
    /// A recording: what was asked, and what mod.io answered.
    /// </summary>
    public static JsonNode Recording(string name)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RecordedFolder, $"{name}.json")))
            ?? throw new InvalidDataException($"The recording {name} is empty.");

    /// <summary>
    /// The body mod.io answered a recording with.
    /// </summary>
    public static JsonNode Body(string name) => Recording(name)["response"]!["body"]!;

    /// <summary>
    /// From now on, mod.io answers the recorded request with the recorded answer, in place of any other recording of it.
    /// </summary>
    public void Serve(string name)
    {
        JsonNode request = Recording(name)["request"]!;
        _served[Key(
            request["method"]!.GetValue<string>(),
            request["path"]!.GetValue<string>(),
            Pairs(request["query"]?.GetValue<string>()),
            request["platform"]?.GetValue<string>())] = name;
    }

    /// <summary>
    /// The whole recorded API: every recording but those of the hosts and credentials tried, and those of a credential
    /// refused, which answer a request another answers too.
    /// </summary>
    public void ServeAll()
    {
        string[] others = ["me-refused", "download-none", "user"];
        foreach (string name in Recordings.Where(name => !name.StartsWith("probe-", StringComparison.Ordinal)
                     && !name.EndsWith("-key", StringComparison.Ordinal)
                     && !others.Contains(name)))
        {
            Serve(name);
        }
    }

    /// <summary>
    /// From now on, mod.io answers requests for <paramref name="path"/> as the test sets up, before any recording.
    /// </summary>
    public void Answer(string path, Func<IResponseBuilder, IResponseBuilder> response)
        => _server.Given(Request.Create().UsingGet().WithPath(path)).AtPriority(1).RespondWith(response(Response.Create()));

    /// <summary>
    /// From now on, mod.io answers requests for <paramref name="path"/> that have <paramref name="parameter"/> as the
    /// test sets up, before those without it.
    /// </summary>
    public void Answer(string path, string parameter, Func<IResponseBuilder, IResponseBuilder> response)
        => _server.Given(Request.Create().UsingGet().WithPath(path).WithParam(parameter)).AtPriority(0).RespondWith(response(Response.Create()));

    /// <summary>
    /// From now on, mod.io answers requests for <paramref name="path"/> with the recording's answer, before any other.
    /// </summary>
    public void Answer(string path, string recording)
    {
        JsonNode response = Recording(recording)["response"]!;
        Answer(path, answer =>
        {
            answer = answer.WithStatusCode(response["status"]!.GetValue<int>());
            foreach ((string header, JsonNode? value) in response["headers"]!.AsObject())
            {
                answer = answer.WithHeader(header, value!.GetValue<string>());
            }

            return answer.WithBody(Text(response["body"]));
        });
    }

    /// <summary>
    /// The source's services, as the hosts register them, calling this mod.io: <c>AddSourceHttpClients()</c>, with no
    /// account linked or with <paramref name="linked"/>, an API key or a token, then <c>AddModIoSource()</c>, with as many
    /// requests a minute as the tests need, every API here unless <paramref name="configure"/> says otherwise, on
    /// <see cref="Clock"/>. Retries don't wait.
    /// </summary>
    public ServiceProvider BuildServices(
        int retries = 3,
        string? linked = null,
        int requestsPerMinute = 10_000,
        Func<ModIoOptions, ModIoOptions>? configure = null)
    {
        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddSingleton<TimeProvider>(Clock);
        if (linked is null)
        {
            services.AddSingleton<ICredentialProvider, NoLinkedAccount>();
        }
        else
        {
            services.AddSingleton(provider => new LinkedCredentials(provider, linked));
            services.AddSingleton<ICredentialProvider>(provider => provider.GetRequiredService<LinkedCredentials>());
        }

        services.AddSourceHttpClients(new SourceHttpOptions
        {
            MaxRetryAttempts = retries,
            RetryDelay = TimeSpan.Zero,
            AttemptTimeout = TimeSpan.FromSeconds(10),
        });
        ModIoOptions options = new()
        {
            ApiAddress = ApiAddress,
            GameApiAddress = ApiAddress.AbsoluteUri,
            RequestsPerMinute = requestsPerMinute,
        };
        services.AddModIoSource(configure?.Invoke(options) ?? options);
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        Logs.Dispose();
    }

    /// <summary>
    /// A request as it's matched: its method, path, query without the API key, its parameters in order, and its platform.
    /// </summary>
    private static string Key(string method, string path, IEnumerable<(string Name, string Value)> query, string? platform)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{method.ToUpperInvariant()} {path}?{string.Join('&', query.Where(pair => pair.Name != "api_key").Order().Select(pair => $"{pair.Name}={pair.Value}"))} [{platform}]");

    private static List<(string Name, string Value)> Pairs(string? query)
        => [.. (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Select(parts => (Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts.Length > 1 ? parts[1] : string.Empty)))];

    private static long? Long(JsonNode? node, string name) => node?[name]?.GetValue<long>();

    private static string Text(JsonNode? body)
        => body is JsonValue text && text.TryGetValue(out string? plain) ? plain : body?.ToJsonString() ?? string.Empty;

    private IResponseMessage Answer(IRequestMessage request)
    {
        List<(string Name, string Value)> query = Pairs(request.RawQuery);
        string? platform = request.Headers?.FirstOrDefault(header => string.Equals(header.Key, PlatformHeader, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
        string key = Key(request.Method, request.Path, query, platform);
        if (_served.TryGetValue(key, out string? name))
        {
            JsonNode response = Recording(name)["response"]!;
            ResponseMessage answer = Message(response["status"]!.GetValue<int>(), Text(response["body"]), contentType: null);
            foreach ((string header, JsonNode? value) in response["headers"]!.AsObject())
            {
                answer.AddHeader(header, value!.GetValue<string>());
            }

            return answer;
        }

        JsonObject? faked = platform is null && request.Method == "GET" ? Faked(request.Path, query) : null;
        return faked is null
            ? Message(501, new JsonObject { ["error"] = new JsonObject { ["code"] = 501, ["message"] = $"Not recorded: {key}" } }.ToJsonString())
            : Message(200, faked.ToJsonString());
    }

    private ResponseMessage Message(int status, string body, string? contentType = "application/json")
    {
        ResponseMessage answer = new()
        {
            StatusCode = status,
            BodyData = new BodyData
            {
                BodyAsString = MovedHere(body),
                DetectedBodyType = BodyType.String,
                Encoding = Encoding.UTF8,
            },
        };
        if (contentType is not null)
        {
            answer.AddHeader("Content-Type", contentType);
        }

        return answer;
    }

    /// <summary>
    /// What mod.io answered, its links to its API made links here.
    /// </summary>
    private string MovedHere(string body) => ApiLink().Replace(body, ApiAddress.AbsoluteUri);

    /// <summary>
    /// An answer made of what was recorded, for what wasn't asked word for word; null for what can't be.
    /// </summary>
    private static JsonObject? Faked(string path, List<(string Name, string Value)> query)
    {
        Dictionary<string, string> asked = query.GroupBy(pair => pair.Name).ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        return path switch
        {
            $"/v1/games/{Game}/mods" when asked.Keys.All(ModFilters.Contains) => Mods(asked),
            "/v1/games" when asked is { Count: 4 } && asked.GetValueOrDefault("api_access_options-bitwise-and") == "1"
                && asked.GetValueOrDefault("_sort") == "name" && asked.GetValueOrDefault("_limit") == "100" && asked.ContainsKey("_offset")
                => Page([], int.Parse(asked["_offset"], CultureInfo.InvariantCulture), 100, Body("games-open-1")["result_total"]!.GetValue<int>()),
            $"/v1/games/{Game}/mods/events" when asked.GetValueOrDefault("event_type") == "MODFILE_CHANGED" && asked.ContainsKey("date_added-min")
                && asked.Keys.All(name => name is "event_type" or "date_added-min" or "_limit" or "_offset")
                => Changes(asked),
            _ => null,
        };
    }

    /// <summary>
    /// The recorded mods of the game, narrowed and ordered as asked, a page of them.
    /// </summary>
    private static JsonObject Mods(Dictionary<string, string> asked)
    {
        IEnumerable<JsonNode> mods = Recordings
            .Where(name => name.StartsWith("mods-", StringComparison.Ordinal) || name.StartsWith("search-", StringComparison.Ordinal))
            .SelectMany(name => Body(name)["data"]!.AsArray().OfType<JsonNode>())
            .DistinctBy(mod => Long(mod, "id"));
        if (asked.TryGetValue("_q", out string? words))
        {
            mods = mods.Where(mod => words.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(word => mod["name"]!.GetValue<string>().Contains(word, StringComparison.OrdinalIgnoreCase)));
        }

        if (asked.TryGetValue("submitted_by_display_name", out string? author))
        {
            mods = mods.Where(mod => string.Equals(mod["submitted_by"]?["username"]?.GetValue<string>(), author, StringComparison.OrdinalIgnoreCase));
        }

        if (asked.TryGetValue("submitted_by", out string? member))
        {
            mods = mods.Where(mod => Long(mod["submitted_by"], "id")?.ToString(CultureInfo.InvariantCulture) == member);
        }

        if (asked.TryGetValue("tags", out string? tag))
        {
            mods = mods.Where(mod => mod["tags"]!.AsArray().Any(listed => string.Equals(listed?["name"]?.GetValue<string>(), tag, StringComparison.OrdinalIgnoreCase)));
        }

        if (asked.TryGetValue("date_updated-min", out string? updated))
        {
            mods = mods.Where(mod => Long(mod, "date_updated") >= long.Parse(updated, CultureInfo.InvariantCulture));
        }

        if (asked.TryGetValue("name_id", out string? named))
        {
            mods = mods.Where(mod => string.Equals(mod["name_id"]?.GetValue<string>(), named, StringComparison.OrdinalIgnoreCase));
        }

        string sort = asked.GetValueOrDefault("_sort") ?? "id";
        Func<JsonNode, IComparable?> column = Columns[sort.TrimStart('-')];
        JsonNode[] ordered = sort.StartsWith('-')
            ? [.. mods.OrderByDescending(column).ThenByDescending(mod => Long(mod, "id"))]
            : [.. mods.OrderBy(column).ThenBy(mod => Long(mod, "id"))];
        int offset = asked.TryGetValue("_offset", out string? from) ? int.Parse(from, CultureInfo.InvariantCulture) : 0;
        int limit = asked.TryGetValue("_limit", out string? most) ? int.Parse(most, CultureInfo.InvariantCulture) : 100;
        return Page([.. ordered.Skip(offset).Take(limit)], offset, limit, ordered.Length);
    }

    /// <summary>
    /// The recorded file changes since the time asked, a page of them.
    /// </summary>
    private static JsonObject Changes(Dictionary<string, string> asked)
    {
        long since = long.Parse(asked["date_added-min"], CultureInfo.InvariantCulture);
        JsonNode[] changes = [.. Body("events-files")["data"]!.AsArray().OfType<JsonNode>().Where(change => Long(change, "date_added") >= since)];
        int offset = asked.TryGetValue("_offset", out string? from) ? int.Parse(from, CultureInfo.InvariantCulture) : 0;
        int limit = asked.TryGetValue("_limit", out string? most) ? int.Parse(most, CultureInfo.InvariantCulture) : 100;
        return Page([.. changes.Skip(offset).Take(limit)], offset, limit, changes.Length);
    }

    private static JsonObject Page(JsonNode[] data, int offset, int limit, int total) => new()
    {
        ["data"] = new JsonArray([.. data.Select(record => record.DeepClone())]),
        ["result_count"] = data.Length,
        ["result_offset"] = offset,
        ["result_limit"] = limit,
        ["result_total"] = total,
    };

    /// <summary>
    /// mod.io's API on any of its hosts.
    /// </summary>
    [GeneratedRegex(@"https://(?:[gu]-[0-9]+\.modapi\.io|api\.mod\.io)/v1/", RegexOptions.CultureInvariant)]
    private static partial Regex ApiLink();
}
