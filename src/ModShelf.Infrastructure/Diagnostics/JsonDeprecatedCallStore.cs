using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Diagnostics;
using ModShelf.Application.Diagnostics.Ports;

namespace ModShelf.Infrastructure.Diagnostics;

/// <summary>
/// Keeps the count of deprecated calls in a JSON file of its own, <c>deprecated-calls.json</c> beside
/// <c>settings.json</c>, by the argument's name:
/// <code>
/// {
///   "download.link": {
///     "code": "DL_LINK_ARG",
///     "count": 3,
///     "first": "2026-11-01T09:12:00Z",
///     "last": "2026-11-03T22:31:00Z",
///     "client": "claude-ai"
///   }
/// }
/// </code>
/// Several processes count at once, such as two MCP servers, so each opens the file alone, waiting a moment for another
/// to finish. A file that can't be read is forgotten, and started again when a call is next counted; a name whose count
/// isn't what it should be is left out, and starts again. What else the file holds stays.
/// </summary>
internal sealed partial class JsonDeprecatedCallStore : IDeprecatedCallStore
{
    private const string Code = "code";
    private const string Count = "count";
    private const string First = "first";
    private const string Last = "last";
    private const string Client = "client";
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    // How long to wait for another process counting: 100 tries, 20 ms apart, about 2 seconds.
    private const int Tries = 100;
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(20);

    private static readonly JsonDocumentOptions Reading = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static readonly JsonSerializerOptions Writing = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<JsonDeprecatedCallStore> _logger;

    /// <param name="path">The file's absolute path. It's created when a call is first counted.</param>
    public JsonDeprecatedCallStore(string path, ILogger<JsonDeprecatedCallStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' is not an absolute path.", nameof(path));
        }

        _path = path;
        _logger = logger;
    }

    public string Location => _path;

    public async Task<IReadOnlyList<DeprecatedCall>> LoadAsync(CancellationToken cancellationToken)
    {
        string text;
        try
        {
            FileStream file = await OpenAsync(FileMode.Open, FileAccess.Read, FileShare.Read, cancellationToken).ConfigureAwait(false);
            await using (file.ConfigureAwait(false))
            {
                text = await ReadAsync(file, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Such as a folder in its place, or a file another program keeps locked.
            LogUnreadable(_logger, _path, exception.Message);
            return [];
        }

        // A file just made, before a count was written to it, is empty: nothing is counted yet.
        return !string.IsNullOrWhiteSpace(text) && Parse(text) is { } calls
            ? [.. calls.Select(call => call.Value is JsonObject entry ? CallOf(call.Key, entry) : null).OfType<DeprecatedCall>().OrderBy(call => call.Name, StringComparer.Ordinal)]
            : [];
    }

    public async Task AddAsync(string name, string code, string? client, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            FileStream file = await OpenAsync(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, cancellationToken).ConfigureAwait(false);
            await using (file.ConfigureAwait(false))
            {
                string text = await ReadAsync(file, cancellationToken).ConfigureAwait(false);

                JsonObject calls = (string.IsNullOrWhiteSpace(text) ? null : Parse(text)) ?? [];
                DeprecatedCall? before = calls[name] is JsonObject entry ? CallOf(name, entry) : null;
                calls[name] = new JsonObject
                {
                    [Code] = code,
                    [Count] = before is null ? 1 : before.Count == long.MaxValue ? long.MaxValue : before.Count + 1,
                    [First] = TimeText(before?.First ?? at),
                    [Last] = TimeText(at),
                    [Client] = client,
                };

                byte[] written = new UTF8Encoding(false).GetBytes(calls.ToJsonString(Writing) + Environment.NewLine);
                file.SetLength(0);
                await file.WriteAsync(written, cancellationToken).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"The deprecated call couldn't be counted in {_path}: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Such as <c>2026-11-03T22:31:00Z</c>, in UTC.
    /// </summary>
    private static string TimeText(DateTimeOffset at) => at.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// The call counted under <paramref name="name"/>; null when it isn't what it should be.
    /// </summary>
    private static DeprecatedCall? CallOf(string name, JsonObject entry)
        => !string.IsNullOrWhiteSpace(name)
            && Text(entry[Code]) is { } code && !string.IsNullOrWhiteSpace(code)
            && entry[Count] is JsonValue count && count.TryGetValue(out long counted) && counted >= 1
            && Time(entry[First]) is { } first
            && Time(entry[Last]) is { } last
            && (entry[Client] is null || Text(entry[Client]) is not null)
                ? new DeprecatedCall(name, code, counted, first, last, Text(entry[Client]))
                : null;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static DateTimeOffset? Time(JsonNode? node)
        => Text(node) is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset at)
                ? at
                : null;

    private static async Task<string> ReadAsync(FileStream file, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(file, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the file as <paramref name="share"/> lets other processes, waiting while another has it open as it doesn't
    /// let this one.
    /// </summary>
    private async Task<FileStream> OpenAsync(FileMode mode, FileAccess access, FileShare share, CancellationToken cancellationToken)
    {
        for (int tried = 1; ; tried++)
        {
            try
            {
                return new FileStream(_path, mode, access, share);
            }
            catch (IOException exception) when (tried < Tries && exception is not (FileNotFoundException or DirectoryNotFoundException))
            {
                await Task.Delay(Pause, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <returns>The file's object; null when it can't be read, which is logged.</returns>
    private JsonObject? Parse(string text)
    {
        try
        {
            if (JsonNode.Parse(text, documentOptions: Reading) is JsonObject calls)
            {
                return calls;
            }

            LogUnreadable(_logger, _path, "The file must hold a JSON object.");
        }
        catch (JsonException exception)
        {
            LogUnreadable(_logger, _path, exception.Message);
        }

        return null;
    }
}
