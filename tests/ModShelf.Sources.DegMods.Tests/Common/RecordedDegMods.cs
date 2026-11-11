using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Accounts;
using ModShelf.Infrastructure;
using ModShelf.Infrastructure.Sources;
using ModShelf.Sources.Abstractions.Http;
using ModShelf.Sources.DegMods.Api;
using ModShelf.Testing;

using WireMock;
using WireMock.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Types;
using WireMock.Util;

namespace ModShelf.Sources.DegMods.Tests.Common;

/// <summary>
/// DEG Mods as recorded by <c>Fixtures/Record-DegModsFixtures.ps1</c>, served on this computer: its two relays, holding
/// the recorded events and any a test adds, over WebSockets; and the file servers' recorded answers, to which the source's
/// requests for files go whatever their host. The source's services are set up to call them.
/// </summary>
internal sealed class RecordedDegMods : IDisposable
{
    /// <summary>
    /// When the recordings were made, Nov 11, 2026, 10:23 UTC: the tests' clock starts there.
    /// </summary>
    public static readonly DateTimeOffset RecordedAt = new(2026, 11, 11, 10, 23, 0, TimeSpan.Zero);

    /// <summary>
    /// The author of the recorded Fallout: New Vegas mods.
    /// </summary>
    public const string FalloutAuthor = "16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1";

    /// <summary>
    /// Better Newspapers for New Vegas Bounties II: three files, the first two on DEG Mods' server, the first of those on
    /// another too.
    /// </summary>
    public const string Newspapers = "7cce3021-6514-47f7-a054-a28dc1269ff4";

    /// <summary>
    /// Its first file, which <c>blossom.primal.net</c> has too.
    /// </summary>
    public const string NewspapersFile = "550a7a5da7893d23b7e9e5562b1c69b6576476f72f6ef3a9afde9cb27837f08a";

    /// <summary>
    /// Nipton Rebuilt Remade: two files only DEG Mods' server has, and two on archive.org; an older listing too.
    /// </summary>
    public const string Nipton = "4e09a170-8393-4368-b59c-6c54db890857";

    /// <summary>
    /// Its first file, which only DEG Mods' server has.
    /// </summary>
    public const string NiptonFile = "8890132ecb96efb82f9b9ed56a6e41a3c6acb7b5a238192473cd2271d5effa26";

    /// <summary>
    /// Smook's Police Gear Pack: a file on DEG Mods' old server and catbox.moe; its older listing on the other relay says
    /// it moved.
    /// </summary>
    public const string PoliceGear = "7162b89c-da8a-4889-b4df-db905d8ecf03";

    /// <summary>
    /// The author of the recorded Starfield mods VGB…, older listings only.
    /// </summary>
    public const string VgbAuthor = "1b939fa858ed76e8daec4f31f1c65343c0be9467cecb50c73d5bcf2c1ebdc679";

    /// <summary>
    /// VGB Fed and Hydration Duration Increase: an older listing, its file on catbox.moe.
    /// </summary>
    public const string FedAndHydration = "49930431-b762-4d6b-b22b-6488f94e5d2b";

    /// <summary>
    /// The author of Street Fighter 6's Reject Wifi.
    /// </summary>
    public const string WifiAuthor = "c3712e02c22d4afba641ba8a2f9da724c1e4cf67ed97c4965eb762d603822eb9";

    /// <summary>
    /// Reject Wifi: two versions, both on DEG Mods' server.
    /// </summary>
    public const string RejectWifi = "1d5868f2-8fa3-4b38-b135-64843c076f9b";

    /// <summary>
    /// The author of Starfield's music replacers: eight mods, and an older listing that moved.
    /// </summary>
    public const string MusicAuthor = "b20828742c7a62a1c4547508c5d141eae619c27674bba56846b7cc7ac848020a";

    /// <summary>
    /// Halo &amp; Doom OSTs: its file is on MediaFire.
    /// </summary>
    public const string HaloAndDoom = "7906514a-430a-447e-9baa-23891a966ead";

    /// <summary>
    /// The test posts' author: one in the admin's blocked mods, one given a moderation tag.
    /// </summary>
    public const string TestPostAuthor = "0001c40591acd6a4e6c524f1b3ac77b2e8399002b38c9734ba610b1d985ae2c2";

    public const string BlockedPost = "03d5d7e1-5338-488d-bb8c-da251e62ce27";

    public const string TaggedPost = "b0c9ee94-f19d-4b2c-b8de-10e51cd93e4d";

    /// <summary>
    /// The game the tests make up, with more mods than a search's page holds, by <see cref="Maker"/>.
    /// </summary>
    public const string TestGame = "ModShelf Test Game";

    /// <summary>
    /// How many mods the made-up game has, and how many of them are for adults.
    /// </summary>
    public const int TestGameMods = 60;

    public const int TestGameAdultMods = 6;

    private static readonly string RecordedFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Recorded");

    private readonly WireMockServer _server = WireMockServer.Start();

    public RecordedDegMods()
    {
        Brs = Relay("brs.degmods.com");
        Main = Relay("relay.degmods.com");
        foreach (string name in Recordings.Where(name => !name.StartsWith("relay-", StringComparison.Ordinal)))
        {
            ServeFile(name);
        }
    }

    /// <summary>
    /// DEG Mods' newer relay, which serves mods, mod jams and moderation tags only.
    /// </summary>
    public FakeRelay Brs { get; }

    /// <summary>
    /// DEG Mods' older relay, which serves everything, its admin's lists among it.
    /// </summary>
    public FakeRelay Main { get; }

    /// <summary>
    /// Who made the made-up game's mods.
    /// </summary>
    public TestAuthor Maker { get; } = new(1);

    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>
    /// Every request the file servers got, oldest first.
    /// </summary>
    public IReadOnlyList<ILogEntry> Requests => [.. _server.LogEntries.Where(entry => entry.RequestMessage?.Path?.StartsWith("/relay", StringComparison.Ordinal) == false)];

    /// <summary>
    /// Every recording, by its name, such as <c>catbox-get</c>.
    /// </summary>
    public static IReadOnlyList<string> Recordings
        => [.. Directory.GetFiles(RecordedFolder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)];

    /// <summary>
    /// A recording, as it was written.
    /// </summary>
    public static JsonNode Recording(string name)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(RecordedFolder, $"{name}.json")))
            ?? throw new InvalidDataException($"The recording {name} is empty.");

    /// <summary>
    /// The events a relay sent in its recording, each once.
    /// </summary>
    public static IReadOnlyList<NostrEvent> RecordedEvents(string host)
        => [
            .. Recording($"relay-{host}")["exchanges"]!.AsArray()
                .SelectMany(exchange => exchange!["messages"]!.AsArray())
                .Where(message => message![0]!.GetValue<string>() == "EVENT")
                .Select(message => TestAuthor.Read(message![2]!))
                .DistinctBy(nostrEvent => nostrEvent.Id),
        ];

    /// <summary>
    /// The relay's address here.
    /// </summary>
    public Uri Address(FakeRelay relay) => new($"{_server.Url!.Replace("http://", "ws://", StringComparison.Ordinal)}/relay/{relay.Host}");

    /// <summary>
    /// Adds the made-up game: <see cref="TestGameMods"/> mods by <see cref="Maker"/>, a day apart, the last made a day
    /// before <see cref="RecordedAt"/>, each with a file on <c>blossom.primal.net</c>; every tenth one for adults.
    /// </summary>
    public void AddTestGame()
    {
        long start = RecordedAt.AddDays(-TestGameMods - 1).ToUnixTimeSeconds();
        for (int index = 0; index < TestGameMods; index++)
        {
            string sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"file {index}")));
            string[][] adult = index % 10 == 9 ? [["content-warning", "nsfw"]] : [];
            Brs.Add(Maker.Mod(
                $"test-mod-{index:00}",
                $"{(index % 2 == 0 ? "Even" : "Odd")} Test Mod {index:00}",
                TestGame,
                start + (index * 86400),
                [($"https://blossom.primal.net/{sha256}.zip", sha256)],
                [.. adult, ["t", "testing"]]));
        }
    }

    /// <summary>
    /// The source's services, as the hosts register them, calling these relays and file servers:
    /// <c>AddSourceHttpClients()</c> with no account linked, then <c>AddDegModsSource()</c>, on <paramref name="clock"/>
    /// or a test clock at <see cref="RecordedAt"/>. Retries don't wait.
    /// </summary>
    public ServiceProvider BuildServices(Func<DegModsOptions, DegModsOptions>? configure = null, TimeProvider? clock = null, int retries = 3)
    {
        DegModsOptions options = new() { Relays = [Address(Brs), Address(Main)] };
        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddSingleton(clock ?? new FakeTimeProvider(RecordedAt));
        services.AddSingleton<ICredentialProvider, NoLinkedAccount>();
        services.AddSourceHttpClients(new SourceHttpOptions
        {
            MaxRetryAttempts = retries,
            RetryDelay = TimeSpan.Zero,
            AttemptTimeout = TimeSpan.FromSeconds(10),
        });
        services.AddDegModsSource(configure?.Invoke(options) ?? options);
        services.AddHttpClient(SourceHttpClients.NameFor(DegModsSource.DegModsId)).AddHttpMessageHandler(() => new ToRecorded(new Uri(_server.Url!)));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// From now on, the file server at <paramref name="url"/>'s host answers <paramref name="method"/> for its path with
    /// <paramref name="status"/>, before any recording.
    /// </summary>
    public void Answer(string method, Uri url, int status)
        => _server
            .Given(Request.Create().UsingMethod(method).WithPath($"/{url.Host}{url.AbsolutePath}"))
            .AtPriority(0)
            .RespondWith(Response.Create().WithStatusCode(status));

    /// <summary>
    /// The paths the file servers were asked for, as <c>&lt;host&gt;&lt;path&gt;</c>, oldest first.
    /// </summary>
    public IReadOnlyList<string> Asked => [.. Requests.Select(entry => $"{entry.RequestMessage!.Method} {entry.RequestMessage.Path!.TrimStart('/')}")];

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        Logs.Dispose();
    }

    /// <summary>
    /// A relay holding what it sent in its recording, and refusing what it refused there, served here.
    /// </summary>
    private FakeRelay Relay(string host)
    {
        FakeRelay relay = new(host);
        foreach (NostrEvent nostrEvent in RecordedEvents(host))
        {
            relay.Add(nostrEvent);
        }

        foreach (JsonNode? exchange in Recording($"relay-{host}")["exchanges"]!.AsArray())
        {
            if (exchange!["messages"]!.AsArray().Any(message => message![0]!.GetValue<string>() == "CLOSED"))
            {
                foreach (JsonNode? kind in exchange["request"]![2]!["kinds"]!.AsArray())
                {
                    relay.RefusedKinds.Add(kind!.GetValue<int>());
                }
            }
        }

        _server
            .Given(Request.Create().WithPath($"/relay/{host}").WithWebSocketUpgrade())
            .RespondWith(Response.Create().WithWebSocket(socket => socket.WithMessageHandler(async (message, context) =>
            {
                relay.UserAgents.Enqueue(context.HttpContext.Request.Headers.UserAgent.ToString());
                foreach (string answer in relay.Answer(message.Text ?? string.Empty))
                {
                    await context.SendAsync(answer, CancellationToken.None).ConfigureAwait(false);
                }
            })));
        return relay;
    }

    /// <summary>
    /// A file server's recorded answer, at <c>/&lt;host&gt;&lt;path&gt;</c>, for the method it was asked with.
    /// </summary>
    private void ServeFile(string name)
    {
        JsonNode recording = Recording(name);
        Uri url = new(recording["request"]!["url"]!.GetValue<string>());
        JsonNode response = recording["response"]!;
        _server
            .Given(Request.Create().UsingMethod(recording["request"]!["method"]!.GetValue<string>()).WithPath($"/{url.Host}{url.AbsolutePath}"))
            .RespondWith(Response.Create().WithCallback(_ => Answer(response)));
    }

    private static ResponseMessage Answer(JsonNode response)
    {
        byte[] body = response["bodyBase64"] is { } base64 ? Convert.FromBase64String(base64.GetValue<string>())
            : response["body"] is { } text ? Encoding.UTF8.GetBytes(text.GetValue<string>())
            : [];
        ResponseMessage answer = new()
        {
            StatusCode = response["status"]!.GetValue<int>(),
            BodyData = new BodyData { BodyAsBytes = body, DetectedBodyType = BodyType.Bytes },
        };
        foreach ((string header, JsonNode? value) in response["headers"]!.AsObject())
        {
            if (header != "Content-Length")
            {
                answer.AddHeader(header, value!.GetValue<string>());
            }
        }

        return answer;
    }

    /// <summary>
    /// Sends every request of the source's client here, as <c>/&lt;host&gt;&lt;path&gt;</c>, so a file server's link
    /// reaches its recording.
    /// </summary>
    private sealed class ToRecorded(Uri server) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // A retry sends the request again, already sent here.
            Uri asked = request.RequestUri!;
            if (asked.Authority != server.Authority)
            {
                request.RequestUri = new Uri(server, $"/{asked.Host}{asked.PathAndQuery}");
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
