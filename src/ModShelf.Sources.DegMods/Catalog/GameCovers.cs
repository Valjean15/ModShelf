using System.Net;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;

using ModShelf.Sources.Abstractions.Http;
using ModShelf.Sources.DegMods.Api;
using ModShelf.Sources.DegMods.Downloads;

namespace ModShelf.Sources.DegMods.Catalog;

/// <summary>
/// The covers of DEG Mods' games, from the games database its admin publishes (kind 30078 <c>games-db</c>): CSV files on
/// Blossom servers, each named by its SHA-256, listing each game's name with a wide picture and its box art, a later
/// file's row in place of an earlier one's, as DEG Mods' site reads them (ADR 0093). Read once per run, the first time
/// they're asked for, while the games are; only the rows of the games asked for are kept.
/// </summary>
internal sealed partial class GameCovers : IDisposable
{
    /// <summary>
    /// The admin's games database (kind 30078).
    /// </summary>
    public const string DatabaseList = "games-db";

    /// <summary>
    /// The most a CSV file may hold: DEG Mods' are about 3.4 MB each.
    /// </summary>
    internal const int MostBytes = 32 * 1024 * 1024;

    private const int MaxRedirects = 5;

    private static readonly IReadOnlyDictionary<string, Uri> None = new Dictionary<string, Uri>();

    private readonly DegModsRelays _relays;
    private readonly DegModsOptions _options;
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<GameCovers> _logger;
    private readonly SemaphoreSlim _reading = new(1, 1);
    private Dictionary<string, Uri>? _covers;

    public GameCovers(DegModsRelays relays, DegModsOptions options, IHttpClientFactory clients, ILogger<GameCovers> logger)
    {
        ArgumentNullException.ThrowIfNull(relays);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(logger);

        _relays = relays;
        _options = options;
        _clients = clients;
        _logger = logger;
    }

    /// <summary>
    /// The covers of the games <paramref name="keys"/> gives, by their keys: each game's wide picture, or else its box art;
    /// none for a game the database doesn't have, and none at all for a host that shows none
    /// (<see cref="DegModsOptions.ReadsGameCovers"/>). The first time, the database is read while the keys are; when it
    /// can't be, there are none, and it's read again the next time.
    /// </summary>
    /// <param name="keys">The keys of the games, as <see cref="ModEvents.GameKey"/> makes them, once they're known.</param>
    /// <exception cref="HttpRequestException">Reading the keys did.</exception>
    public async Task<IReadOnlyDictionary<string, Uri>> ReadAsync(Task<IReadOnlySet<string>> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (!_options.ReadsGameCovers)
        {
            return None;
        }

        if (_covers is { } kept)
        {
            return kept;
        }

        await _reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_covers is { } read)
            {
                return read;
            }

            using CancellationTokenSource reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<IReadOnlyList<byte[]>?> files = FilesAsync(reading.Token);
            IReadOnlySet<string> wanted;
            try
            {
                wanted = await keys.ConfigureAwait(false);
            }
            catch
            {
                await reading.CancelAsync().ConfigureAwait(false);
                await ((Task)files).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                throw;
            }

            if (await files.ConfigureAwait(false) is not { } database)
            {
                return None;
            }

            _covers = Covers(database, wanted);
            LogRead(_logger, _covers.Count, wanted.Count);
            return _covers;
        }
        finally
        {
            _reading.Release();
        }
    }

    public void Dispose() => _reading.Dispose();

    /// <summary>
    /// The fields of a CSV line: separated by commas, a field in quotes holding commas and doubled quotes.
    /// </summary>
    internal static IReadOnlyList<string> Fields(string line)
    {
        List<string> fields = [];
        StringBuilder field = new();
        bool quoted = false;
        for (int at = 0; at < line.Length; at++)
        {
            char character = line[at];
            if (quoted && character == '"' && at + 1 < line.Length && line[at + 1] == '"')
            {
                field.Append('"');
                at++;
            }
            else if (character == '"')
            {
                quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }

    /// <summary>
    /// The covers of the games wanted, from the files in order, a later file's row in place of an earlier one's.
    /// </summary>
    private static Dictionary<string, Uri> Covers(IReadOnlyList<byte[]> files, IReadOnlySet<string> wanted)
    {
        Dictionary<string, Uri> covers = new(StringComparer.Ordinal);
        foreach (byte[] file in files)
        {
            using StringReader lines = new(Encoding.UTF8.GetString(file));

            // The first line names the columns: Game Name, 16 by 9 image, Boxart image.
            lines.ReadLine();
            while (lines.ReadLine() is { } line)
            {
                IReadOnlyList<string> fields = Fields(line);
                string key = ModEvents.GameKey(fields[0]);
                if (key.Length == 0 || !wanted.Contains(key))
                {
                    continue;
                }

                if ((Picture(fields, 1) ?? Picture(fields, 2)) is { } cover)
                {
                    covers[key] = cover;
                }
                else
                {
                    covers.Remove(key);
                }
            }
        }

        return covers;
    }

    /// <summary>
    /// The picture in the field: a web address on another computer than this one or its network; null otherwise.
    /// </summary>
    private static Uri? Picture(IReadOnlyList<string> fields, int at)
        => at < fields.Count
            && Uri.TryCreate(fields[at].Trim(), UriKind.Absolute, out Uri? picture)
            && (picture.Scheme == Uri.UriSchemeHttps || picture.Scheme == Uri.UriSchemeHttp)
            && !FileHosts.IsOnThisNetwork(picture)
                ? picture
                : null;

    /// <summary>
    /// The database's files, in the order its newest event lists them; those no server gives are passed over. Null when
    /// the event can't be read.
    /// </summary>
    private async Task<IReadOnlyList<byte[]>?> FilesAsync(CancellationToken cancellationToken)
    {
        NostrFilter filter = new()
        {
            Kinds = [Moderation.ListKind],
            Authors = [_options.Admin],
            Tags = new Dictionary<char, IReadOnlyList<string>> { ['d'] = [DatabaseList] },
        };
        IReadOnlyList<NostrEvent> events;
        try
        {
            events = await _relays.ReadAsync([filter], cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            LogDatabaseUnread(_logger, exception);
            return null;
        }

        if (events
                .Where(nostrEvent => nostrEvent.PubKey == _options.Admin && nostrEvent.Kind == Moderation.ListKind && nostrEvent.D == DatabaseList)
                .MaxBy(nostrEvent => nostrEvent.CreatedAt) is not { } newest)
        {
            LogNoDatabase(_logger);
            return null;
        }

        string[] hashes =
        [
            .. newest.FirstValues("csv")
                .Select(hash => hash.Trim().ToLowerInvariant())
                .Where(hash => hash.Length == 64 && ModEvents.IsHex(hash))
                .Distinct(StringComparer.Ordinal),
        ];
        byte[]?[] files = await Task.WhenAll(hashes.Select(hash => FileAsync(hash, cancellationToken))).ConfigureAwait(false);
        return [.. files.OfType<byte[]>()];
    }

    /// <summary>
    /// The file with the SHA-256, from the first server not DEG Mods' own that gives it whole; null when none does.
    /// </summary>
    private async Task<byte[]?> FileAsync(string sha256, CancellationToken cancellationToken)
    {
        foreach (Uri server in _options.FileServers.Where(server => !_options.GatedHosts.Contains(server.Host, StringComparer.OrdinalIgnoreCase)))
        {
            if (await DownloadAsync(new Uri(server, sha256), cancellationToken).ConfigureAwait(false) is { } file
                && Convert.ToHexStringLower(SHA256.HashData(file)) == sha256)
            {
                return file;
            }
        }

        LogFileUnread(_logger, sha256[..8]);
        return null;
    }

    /// <summary>
    /// What the address gives, following where it sends ModShelf, at most five times, to web addresses not on this
    /// computer or its network; null when it gives nothing, or more than <see cref="MostBytes"/>.
    /// </summary>
    private async Task<byte[]?> DownloadAsync(Uri link, CancellationToken cancellationToken)
    {
        HttpClient client = _clients.CreateClient(SourceHttpClients.NameFor(DegModsSource.DegModsId));
        Uri address = link;
        for (int sent = 0; sent <= MaxRedirects; sent++)
        {
            if (FileHosts.IsOnThisNetwork(address))
            {
                return null;
            }

            try
            {
                using HttpResponseMessage response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    address = new Uri(address, location);
                    if (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp)
                    {
                        return null;
                    }

                    continue;
                }

                return response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentLength is null or <= MostBytes
                    ? await ReadAsync(response.Content, cancellationToken).ConfigureAwait(false)
                    : null;
            }
            catch (HttpRequestException exception)
            {
                LogServerFailed(_logger, exception, address.Host);
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// The content, up to <see cref="MostBytes"/>; null for more.
    /// </summary>
    private static async Task<byte[]?> ReadAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream file = new();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (file.Length + read > MostBytes)
            {
                return null;
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return file.ToArray();
    }
}
