using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Logging;

/// <summary>
/// Writes one app's logs to files in a folder, a file a day, such as <c>desktop-20261029.log</c>. A file that's full,
/// or that another process of the app is writing, goes on in the next free one: <c>desktop-20261029-1.log</c>, and so
/// on. The app's files older than <see cref="DaysKept"/> days are deleted when a day's first entry is written.
/// </summary>
/// <remarks>
/// Each entry is written and flushed as it's logged, so a crash loses nothing logged before it. Logging never throws:
/// when the folder or a file can't be written, the logs are dropped until the next day.
/// </remarks>
[ProviderAlias("File")]
internal sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// How many days of logs are kept, counting today.
    /// </summary>
    public const int DaysKept = 7;

    /// <summary>
    /// How large a file grows before the day's logs go on in the next.
    /// </summary>
    public const long DefaultMaxFileSize = 10 * 1024 * 1024;

    private const int MaxFilesADay = 100;
    private const string Extension = ".log";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _folder;
    private readonly string _app;
    private readonly TimeProvider _timeProvider;
    private readonly long _maxFileSize;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    private FileStream? _file;
    private DateOnly? _day;
    private DateOnly? _givenUpOn;
    private int _index;
    private bool _disposed;

    /// <param name="folder">The folder for the files, an absolute path. It's created when the first entry is written.</param>
    /// <param name="app">What the files' names start with, such as <c>desktop</c>.</param>
    /// <param name="maxFileSize">How large a file grows before the day's logs go on in the next.</param>
    public FileLoggerProvider(string folder, string app, TimeProvider timeProvider, long maxFileSize = DefaultMaxFileSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSize);
        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException($"'{folder}' is not an absolute path.", nameof(folder));
        }

        if (app.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"'{app}' can't start a file name.", nameof(app));
        }

        _folder = folder;
        _app = app;
        _timeProvider = timeProvider;
        _maxFileSize = maxFileSize;
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, category => new FileLogger(category, this));

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Close();
        }
    }

    /// <summary>
    /// Writes an entry: when, how bad, where from and what, then the exception, if any, indented beneath.
    /// </summary>
    internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        DateTimeOffset now = _timeProvider.GetLocalNow();
        StringBuilder entry = new();
        entry.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(level.ToString())
            .Append(' ')
            .Append(category)
            .Append('[')
            .Append(eventId.Id.ToString(CultureInfo.InvariantCulture))
            .Append("]: ");
        AppendIndented(entry, message);
        if (exception is not null)
        {
            entry.Append("    ");
            AppendIndented(entry, exception.ToString());
        }

        byte[] bytes = Utf8.GetBytes(entry.ToString());
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            DateOnly day = DateOnly.FromDateTime(now.DateTime);
            try
            {
                if (FileFor(day, bytes.Length) is { } file)
                {
                    file.Write(bytes);
                    file.Flush();
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Close();
                _givenUpOn = day;
            }
        }
    }

    /// <summary>
    /// Appends <paramref name="text"/> and a line break, with each line after the first indented.
    /// </summary>
    private static void AppendIndented(StringBuilder entry, string text)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        entry.Append(lines[0]).Append(Environment.NewLine);
        foreach (string line in lines.Skip(1))
        {
            entry.Append("    ").Append(line).Append(Environment.NewLine);
        }
    }

    /// <summary>
    /// The file to write <paramref name="size"/> bytes of <paramref name="day"/>'s logs to; null when there's none.
    /// </summary>
    private FileStream? FileFor(DateOnly day, int size)
    {
        if (_file is not null && _day == day && (_file.Length == 0 || _file.Length + size <= _maxFileSize))
        {
            return _file;
        }

        if (_givenUpOn == day)
        {
            return null;
        }

        Close();
        if (_day != day)
        {
            _day = day;
            _index = 0;
            Directory.CreateDirectory(_folder);
            DeleteOldFiles(day);
        }

        // Unix shares files only by advisory locks, which only an exclusive one makes other writers respect; readers
        // such as editors take none there, so they can still read the log.
        FileShare share = OperatingSystem.IsWindows() ? FileShare.Read | FileShare.Delete : FileShare.None;
        for (; _index < MaxFilesADay; _index++)
        {
            string name = _index == 0
                ? $"{_app}-{day:yyyyMMdd}{Extension}"
                : string.Create(CultureInfo.InvariantCulture, $"{_app}-{day:yyyyMMdd}-{_index}{Extension}");
            FileStream file;
            try
            {
                file = new FileStream(Path.Combine(_folder, name), FileMode.Append, FileAccess.Write, share);
            }
            catch (IOException)
            {
                // Another process of the app is writing it.
                continue;
            }

            if (file.Length > 0 && file.Length + size > _maxFileSize)
            {
                file.Dispose();
                continue;
            }

            _file = file;
            return file;
        }

        _givenUpOn = day;
        return null;
    }

    /// <summary>
    /// Deletes the app's files from before the days kept. Files of other apps, and other files, stay.
    /// </summary>
    private void DeleteOldFiles(DateOnly today)
    {
        DateOnly oldestKept = today.AddDays(1 - DaysKept);
        foreach (string path in Directory.EnumerateFiles(_folder, $"{_app}-*{Extension}"))
        {
            if (DayOf(Path.GetFileName(path)) is { } day && day < oldestKept)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Tried again the next day.
                }
            }
        }
    }

    /// <returns>
    /// The day of the app's file named <paramref name="name"/>, such as <c>desktop-20261029-1.log</c>; null for a name
    /// the app's files don't have.
    /// </returns>
    private DateOnly? DayOf(string name)
    {
        string prefix = _app + "-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(Extension, StringComparison.Ordinal))
        {
            return null;
        }

        string[] parts = name[prefix.Length..^Extension.Length].Split('-');
        return parts.Length <= 2
            && DateOnly.TryParseExact(parts[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            && (parts.Length == 1 || int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                ? day
                : null;
    }

    private void Close()
    {
        _file?.Dispose();
        _file = null;
    }
}
