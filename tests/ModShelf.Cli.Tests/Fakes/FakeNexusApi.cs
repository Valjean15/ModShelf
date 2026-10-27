using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// The parts of the Nexus Mods API an update check uses, for a library of many mods, served on 127.0.0.1 in the shape of
/// the recordings in ModShelf.Sources.Nexus.Tests: <c>validate.json</c>, each game's <c>updated.json</c> and each mod's
/// <c>files.json</c>. Each mod starts with one main file. Every answer carries the rate-limit headers, counting down
/// from the allowances the test sets, and a request with neither allowance left gets 429, as from Nexus Mods.
/// </summary>
/// <remarks>
/// It's served like the other fakes here rather than by WireMock.Net, whose ASP.NET Core framework would take the
/// Microsoft.Extensions assemblies out of this folder, where the CLI also runs as a process of its own.
/// </remarks>
internal sealed partial class FakeNexusApi : IAsyncDisposable
{
    public const string ApiKey = "nexus-test-key";
    public const int HourlyLimit = 2000;
    public const int DailyLimit = 20000;

    private const string ValidatePath = "/v1/users/validate.json";
    private const int MainCategory = 1;
    private const int OldVersionCategory = 4;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _accepting;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Game, long Mod), List<SiteFile>> _mods = [];
    private readonly List<string> _requests = [];
    private int _hourlyRemaining = HourlyLimit;
    private int _dailyRemaining = DailyLimit;
    private int _refused;

    public FakeNexusApi()
    {
        _listener.Start();
        BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _accepting = AcceptAsync(_stopping.Token);
    }

    /// <summary>
    /// Where the API answers, ending in a slash.
    /// </summary>
    public Uri BaseAddress { get; }

    /// <summary>
    /// The path of every request since the last <see cref="ForgetRequests"/>, but for <c>validate.json</c>, in the
    /// order they came.
    /// </summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests.Where(path => path != ValidatePath)];
            }
        }
    }

    /// <summary>
    /// How many requests got 429.
    /// </summary>
    public int Refused
    {
        get
        {
            lock (_gate)
            {
                return _refused;
            }
        }
    }

    /// <summary>
    /// What's left of the hourly allowance.
    /// </summary>
    public int HourlyRemaining
    {
        get
        {
            lock (_gate)
            {
                return _hourlyRemaining;
            }
        }
    }

    /// <summary>
    /// The name of a mod's file of <paramref name="version"/>.
    /// </summary>
    public static string FileName(long mod, string version) => string.Create(CultureInfo.InvariantCulture, $"mod-{mod}-{version}.zip");

    /// <summary>
    /// The ID of a mod's first main file.
    /// </summary>
    public static long FirstFileId(long mod) => mod * 10;

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        await _accepting.ConfigureAwait(false);
        _listener.Dispose();
        _stopping.Dispose();
    }

    public void ForgetRequests()
    {
        lock (_gate)
        {
            _requests.Clear();
        }
    }

    /// <summary>
    /// Sets what's left of each allowance, such as when an hour starts again.
    /// </summary>
    public void SetRemaining(int hourly, int daily)
    {
        lock (_gate)
        {
            _hourlyRemaining = hourly;
            _dailyRemaining = daily;
        }
    }

    /// <summary>
    /// Adds a mod with one main file, version 1.0, uploaded at <paramref name="uploadedAt"/>.
    /// </summary>
    public void AddMod(string game, long mod, DateTimeOffset uploadedAt)
    {
        lock (_gate)
        {
            _mods[(game, mod)] = [new SiteFile(FirstFileId(mod), "1.0", MainCategory, uploadedAt.ToUnixTimeSeconds(), null)];
        }
    }

    /// <summary>
    /// Uploads a newer main file of the mod, version 1.1, 1.2 and so on, which replaces its main file. The replaced
    /// file becomes an old version.
    /// </summary>
    public void PublishUpdate(string game, long mod, DateTimeOffset uploadedAt)
    {
        lock (_gate)
        {
            List<SiteFile> files = _mods[(game, mod)];
            SiteFile main = files.Single(file => file.Category == MainCategory);
            SiteFile newer = new(main.FileId + 1, string.Create(CultureInfo.InvariantCulture, $"1.{files.Count}"), MainCategory, uploadedAt.ToUnixTimeSeconds(), null);
            files[files.IndexOf(main)] = main with { Category = OldVersionCategory, ReplacedBy = newer.FileId };
            files.Add(newer);
        }
    }

    private static (int Status, object Body) Validate()
        => (200, new Dictionary<string, object?>
        {
            ["user_id"] = 1000001,
            ["key"] = "REDACTED-API-KEY",
            ["name"] = "ModShelfPremiumTester",
            ["is_premium?"] = true,
            ["is_supporter?"] = true,
            ["profile_url"] = "https://avatars.nexusmods.com/1000001/100",
            ["is_supporter"] = true,
            ["is_premium"] = true,
        });

    /// <summary>
    /// A time as Nexus Mods writes its resets, such as <c>2026-10-24 23:00:00 +0000</c>.
    /// </summary>
    private static string ResetText(DateTimeOffset time) => time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss '+0000'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Counts the request against the allowances and answers it, with the rate-limit headers: 401 without the key, 429
    /// when neither allowance has any left, and 404 for what it doesn't serve.
    /// </summary>
    private Response Handle(Request request)
    {
        _requests.Add(request.Path);
        int status;
        object body;
        if (!string.Equals(request.Headers.GetValueOrDefault("apikey"), ApiKey, StringComparison.Ordinal))
        {
            (status, body) = (401, new { Message = "Please provide a valid API Key" });
        }
        else if (_hourlyRemaining <= 0 && _dailyRemaining <= 0)
        {
            _refused++;
            (status, body) = (429, new { Msg = "Rate limit exceeded" });
        }
        else
        {
            _hourlyRemaining = Math.Max(0, _hourlyRemaining - 1);
            _dailyRemaining = Math.Max(0, _dailyRemaining - 1);
            (status, body) = request.Path.Split('/') switch
            {
                ["", "v1", "users", "validate.json"] => Validate(),
                ["", "v1", "games", string game, "mods", "updated.json"] => Updated(game, request.Query["period"]),
                ["", "v1", "games", string game, "mods", string mod, "files.json"]
                    when long.TryParse(mod, NumberStyles.None, CultureInfo.InvariantCulture, out long id) => Files(game, id),
                _ => (404, new { Error = "Not found" }),
            };
        }

        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        return new Response(
            status,
            JsonSerializer.Serialize(body, Json),
            [
                ("x-rl-hourly-limit", HourlyLimit.ToString(CultureInfo.InvariantCulture)),
                ("x-rl-hourly-remaining", _hourlyRemaining.ToString(CultureInfo.InvariantCulture)),
                ("x-rl-hourly-reset", ResetText(now.AddHours(1))),
                ("x-rl-daily-limit", DailyLimit.ToString(CultureInfo.InvariantCulture)),
                ("x-rl-daily-remaining", _dailyRemaining.ToString(CultureInfo.InvariantCulture)),
                ("x-rl-daily-reset", ResetText(now.AddDays(1))),
            ]);
    }

    /// <summary>
    /// The game's mods with activity within the period asked for, as <c>updated.json</c> lists them.
    /// </summary>
    private (int Status, object Body) Updated(string game, string? period)
    {
        long reach = period switch
        {
            "1d" => 86_400,
            "1w" => 7 * 86_400,
            "1m" => 30 * 86_400,
            _ => -1,
        };
        if (reach < 0)
        {
            return (400, new { Message = "period must be 1d, 1w or 1m" });
        }

        long since = TimeProvider.System.GetUtcNow().ToUnixTimeSeconds() - reach;
        return (200, _mods
            .Where(mod => mod.Key.Game == game && mod.Value.Max(file => file.UploadedTimestamp) >= since)
            .OrderBy(mod => mod.Key.Mod)
            .Select(mod => new
            {
                ModId = mod.Key.Mod,
                LatestFileUpdate = mod.Value.Max(file => file.UploadedTimestamp),
                LatestModActivity = mod.Value.Max(file => file.UploadedTimestamp),
            })
            .ToArray());
    }

    private (int Status, object Body) Files(string game, long mod)
    {
        if (!_mods.TryGetValue((game, mod), out List<SiteFile>? files))
        {
            return (404, new { Error = "Mod not found" });
        }

        return (200, new
        {
            Files = files.Select(file => new
            {
                file.FileId,
                Name = string.Create(CultureInfo.InvariantCulture, $"Mod {mod} {file.Version}"),
                file.Version,
                CategoryId = file.Category,
                CategoryName = file.Category == MainCategory ? "MAIN" : "OLD_VERSION",
                FileName = FileName(mod, file.Version),
                file.UploadedTimestamp,
                SizeInBytes = 4,
            }),
            FileUpdates = files
                .Where(file => file.ReplacedBy is not null)
                .Select(file => new { OldFileId = file.FileId, NewFileId = file.ReplacedBy }),
        });
    }

    /// <summary>
    /// A file as the site lists it.
    /// </summary>
    private sealed record SiteFile(long FileId, string Version, int Category, long UploadedTimestamp, long? ReplacedBy);

    private sealed record Request(string Path, NameValueCollection Query, IReadOnlyDictionary<string, string> Headers);

    private sealed record Response(int Status, string Body, IReadOnlyList<(string Name, string Value)> Headers);
}
