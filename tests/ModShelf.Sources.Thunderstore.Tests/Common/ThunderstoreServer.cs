using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Accounts;
using ModShelf.Infrastructure;
using ModShelf.Infrastructure.Sources;
using ModShelf.Sources.Abstractions;
using ModShelf.Testing;

using WireMock.Logging;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace ModShelf.Sources.Thunderstore.Tests.Common;

/// <summary>
/// Thunderstore and its CDN on this computer, served by WireMock.Net: as recorded by
/// <c>Fixtures/Record-ThunderstoreFixtures.ps1</c>, or as a test writes it; and the source's services set up to call it.
/// The CDN's links in the recordings, on <c>ccdn.thunderstore.io</c>, are served here too, at the same paths.
/// </summary>
internal sealed class ThunderstoreServer : IDisposable
{
    /// <summary>
    /// The CDN, as Thunderstore's answers name it.
    /// </summary>
    public const string Cdn = "https://ccdn.thunderstore.io";

    private static readonly string RecordedFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Recorded");

    private readonly WireMockServer _server = WireMockServer.Start();

    /// <summary>
    /// Where Thunderstore answers, ending in a slash.
    /// </summary>
    public Uri BaseAddress => new($"{_server.Url}/");

    public RecordingLoggerProvider Logs { get; } = new();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Every request Thunderstore got, oldest first.
    /// </summary>
    public IReadOnlyList<ILogEntry> Requests => [.. _server.LogEntries];

    /// <summary>
    /// What Thunderstore was asked, each as its path, oldest first.
    /// </summary>
    public IReadOnlyList<string> Asked => [.. Requests.Select(entry => entry.RequestMessage!.Path)];

    /// <summary>
    /// Whether the recordings are there, which <c>Fixtures/Record-ThunderstoreFixtures.ps1</c> makes.
    /// </summary>
    public static bool HasRecordings => Directory.Exists(RecordedFolder) && Directory.GetFiles(RecordedFolder, "*.json").Length > 0;

    /// <summary>
    /// A recording: what was asked, and what Thunderstore answered.
    /// </summary>
    public static JsonNode Recording(string name)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RecordedFolder, $"{name}.json")))
            ?? throw new InvalidDataException($"The recording {name} is empty.");

    /// <summary>
    /// The body Thunderstore answered a recording with.
    /// </summary>
    public static JsonNode Body(string name) => Recording(name)["response"]!["body"]!;

    /// <summary>
    /// Serves every recording: a request that asks something else isn't answered. The index's answer when it was asked
    /// again, with <c>If-Modified-Since</c>, is served only to a request with that header.
    /// </summary>
    public void ServeRecordings()
    {
        foreach (string file in Directory.GetFiles(RecordedFolder, "*.json"))
        {
            Serve(Path.GetFileNameWithoutExtension(file));
        }
    }

    /// <summary>
    /// From now on, Thunderstore answers the recorded request with the recorded answer, the CDN's links pointing here.
    /// </summary>
    public void Serve(string name)
    {
        JsonNode recording = Recording(name);
        JsonNode request = recording["request"]!;
        JsonNode response = recording["response"]!;

        IRequestBuilder match = Request.Create().UsingGet().WithPath(request["path"]!.GetValue<string>());
        bool conditional = request["headers"]?["If-Modified-Since"] is not null;
        match = conditional
            ? match.WithHeader("If-Modified-Since", "*")
            : match.WithHeader("If-Modified-Since", "*", MatchBehaviour.RejectOnMatch);

        IResponseBuilder answer = Response.Create().WithStatusCode(response["status"]!.GetValue<int>());
        foreach ((string header, JsonNode? value) in response["headers"]!.AsObject())
        {
            answer = answer.WithHeader(header, Here(value!.GetValue<string>()));
        }

        // The index's blob, a list of the chunks' links, points here; the packages' icons stay on the CDN.
        JsonNode? body = response["body"];
        string text = body is JsonValue plain && plain.TryGetValue(out string? raw) ? raw : body?.ToJsonString() ?? string.Empty;
        if (body is JsonArray links && links.All(link => link is JsonValue))
        {
            text = Here(text);
        }

        answer = response["gzip"]?.GetValue<bool>() == true ? answer.WithBody(Gzip(text)) : answer.WithBody(text);
        _server.Given(match).AtPriority(conditional ? 1 : 2).RespondWith(answer);
    }

    /// <summary>
    /// Serves the communities list with these communities, each with a cover and an icon.
    /// </summary>
    public void ServeCommunities(params (string Identifier, string Name)[] communities) => ServeCommunities(banners: false, communities);

    /// <summary>
    /// Serves the communities list with these communities, each with a background, a banner when
    /// <paramref name="banners"/>, and an icon.
    /// </summary>
    public void ServeCommunities(bool banners, params (string Identifier, string Name)[] communities)
        => Answer("/api/cyberstorm/community/", HttpStatusCode.OK, new JsonObject
        {
            ["count"] = communities.Length,
            ["next"] = null,
            ["previous"] = null,
            ["results"] = new JsonArray(
            [
                .. communities.Select(community => (JsonNode?)new JsonObject
                {
                    ["name"] = community.Name,
                    ["identifier"] = community.Identifier,
                    ["background_image_url"] = $"{Cdn}/assets/{community.Identifier}/{community.Identifier}-bg-1920x1080.webp",
                    ["hero_image_url"] = banners ? $"{Cdn}/assets/{community.Identifier}/{community.Identifier}-bg-1920x620.webp" : null,
                    ["cover_image_url"] = $"{Cdn}/assets/{community.Identifier}/{community.Identifier}-cover-360x480.webp",
                    ["community_icon_url"] = $"{Cdn}/assets/{community.Identifier}/{community.Identifier}-icon-192x192.webp",
                    ["is_listed"] = true,
                }),
            ]),
        });

    /// <summary>
    /// Serves a community's categories.
    /// </summary>
    public void ServeCategories(string community, params string[] categories)
        => Answer($"/api/cyberstorm/community/{community}/filters/", HttpStatusCode.OK, new JsonObject
        {
            ["package_categories"] = new JsonArray(
            [
                .. categories.Select((category, index) => (JsonNode?)new JsonObject
                {
                    ["id"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["name"] = category,
                    ["slug"] = category.ToLowerInvariant().Replace(' ', '-'),
                }),
            ]),
            ["sections"] = new JsonArray(),
        });

    /// <summary>
    /// Serves a community's listing as Thunderstore does: its index redirects to a blob listing the chunks' links, each
    /// a chunk of packages; gzip-compressed unless <paramref name="gzip"/> is false. The index says it was built at
    /// <paramref name="lastModified"/>, and answers <c>304</c> when asked with <c>If-Modified-Since</c>, unless
    /// <paramref name="changed"/>.
    /// </summary>
    /// <returns>The chunks' paths, in order.</returns>
    public IReadOnlyList<string> ServeListing(
        string community,
        IReadOnlyList<JsonArray> chunks,
        bool gzip = true,
        DateTimeOffset? lastModified = null,
        bool changed = false,
        string? stamp = null)
    {
        string blobs = $"/live/blob-storage/{community}{stamp}";
        string[] paths = [.. chunks.Select((_, index) => $"{blobs}-chunk-{index + 1}.blob")];
        string modified = (lastModified ?? TestPackage.January).ToString("R");
        _server.Given(Request.Create().UsingGet().WithPath($"/c/{community}/api/v1/package-listing-index/")
                .WithHeader("If-Modified-Since", "*", MatchBehaviour.RejectOnMatch))
            .AtPriority(1)
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.Redirect)
                .WithHeader("Location", $"{_server.Url}{blobs}-index.blob")
                .WithHeader("Last-Modified", modified));
        _server.Given(Request.Create().UsingGet().WithPath($"/c/{community}/api/v1/package-listing-index/")
                .WithHeader("If-Modified-Since", "*"))
            .AtPriority(1)
            .RespondWith(changed
                ? Response.Create().WithStatusCode(HttpStatusCode.Redirect)
                    .WithHeader("Location", $"{_server.Url}{blobs}-index.blob")
                    .WithHeader("Last-Modified", modified)
                : Response.Create().WithStatusCode(HttpStatusCode.NotModified).WithHeader("Last-Modified", modified));

        Blob($"{blobs}-index.blob", new JsonArray([.. paths.Select(path => (JsonNode?)$"{_server.Url}{path}")]).ToJsonString(), gzip);
        for (int index = 0; index < chunks.Count; index++)
        {
            Blob(paths[index], chunks[index].ToJsonString(), gzip);
        }

        return paths;
    }

    /// <summary>
    /// Serves a blob of the CDN at <paramref name="path"/>, as <c>application/octet-stream</c>; with
    /// <paramref name="first"/>, before any blob served there already.
    /// </summary>
    public void Blob(string path, string content, bool gzip = true, bool first = false)
        => Blob(path, gzip ? Gzip(content) : Encoding.UTF8.GetBytes(content), first);

    /// <summary>
    /// Serves bytes at <paramref name="path"/>, as <c>application/octet-stream</c>.
    /// </summary>
    public void Blob(string path, byte[] content, bool first = false)
        => _server.Given(Request.Create().UsingGet().WithPath(path)).AtPriority(first ? 0 : 1)
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.OK).WithHeader("Content-Type", "application/octet-stream").WithBody(content));

    /// <summary>
    /// Serves a package as <c>api/experimental/package/{team}/{name}/</c> describes it, listed in these communities.
    /// </summary>
    public void ServeDetail(string team, string name, string latest, params string[] communities)
        => Answer($"/api/experimental/package/{team}/{name}/", HttpStatusCode.OK, new JsonObject
        {
            ["namespace"] = team,
            ["name"] = name,
            ["full_name"] = $"{team}-{name}",
            ["owner"] = team,
            ["rating_score"] = -1,
            ["total_downloads"] = -1,
            ["latest"] = new JsonObject { ["version_number"] = latest, ["dependencies"] = new JsonArray() },
            ["community_listings"] = new JsonArray(
            [
                .. communities.Select(community => (JsonNode?)new JsonObject
                {
                    ["has_nsfw_content"] = false,
                    ["categories"] = new JsonArray(),
                    ["community"] = community,
                    ["review_status"] = "unreviewed",
                }),
            ]),
        });

    /// <summary>
    /// Serves a version's changelog, or none when <paramref name="markdown"/> is null.
    /// </summary>
    public void ServeChangelog(string team, string name, string version, string? markdown)
        => Answer($"/api/experimental/package/{team}/{name}/{version}/changelog/", HttpStatusCode.OK, new JsonObject
        {
            ["markdown"] = markdown,
            ["is_edited"] = false,
            ["edited_at"] = null,
        });

    /// <summary>
    /// From now on, Thunderstore answers GET requests for <paramref name="path"/> with <paramref name="status"/> and
    /// <paramref name="body"/>, before any recording.
    /// </summary>
    public void Answer(string path, HttpStatusCode status, JsonNode? body = null)
        => _server.Given(Request.Create().UsingGet().WithPath(path)).AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body?.ToJsonString() ?? string.Empty));

    /// <summary>
    /// Forgets every answer set up so far, as when Thunderstore changes.
    /// </summary>
    public void Reset() => _server.ResetMappings();

    /// <summary>
    /// The source's services, as the hosts register them, calling this Thunderstore on this clock:
    /// <c>AddSourceHttpClients()</c>, with no account linked, then <c>AddThunderstoreSource()</c>. Retries don't wait.
    /// </summary>
    public ServiceProvider BuildServices(Func<ThunderstoreOptions, ThunderstoreOptions>? configure = null, int retries = 0)
    {
        ThunderstoreOptions options = new() { BaseAddress = BaseAddress };
        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddSingleton<ICredentialProvider, NoLinkedAccount>();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSourceHttpClients(new SourceHttpOptions
        {
            MaxRetryAttempts = retries,
            RetryDelay = TimeSpan.Zero,
            AttemptTimeout = TimeSpan.FromSeconds(30),
        });
        services.AddThunderstoreSource(configure?.Invoke(options) ?? options);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The source from <paramref name="services"/>.
    /// </summary>
    public static ThunderstoreSource SourceOf(IServiceProvider services) => services.GetServices<IModSource>().OfType<ThunderstoreSource>().Single();

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        Logs.Dispose();
    }

    public static byte[] Gzip(string text)
    {
        using MemoryStream packed = new();
        using (GZipStream packing = new(packed, CompressionLevel.Fastest, leaveOpen: true))
        {
            packing.Write(Encoding.UTF8.GetBytes(text));
        }

        return packed.ToArray();
    }

    /// <summary>
    /// The text with the CDN's links pointing here.
    /// </summary>
    private string Here(string text) => text.Replace(Cdn, _server.Url, StringComparison.Ordinal);
}
