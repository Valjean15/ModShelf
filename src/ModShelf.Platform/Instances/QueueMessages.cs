using System.Globalization;
using System.Text.Json;

using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Platform.Instances;

/// <summary>
/// Queue requests and replies as the lines of JSON the queue's pipe carries, and back. Enums go as their names, so a
/// newer app's value an older one doesn't know is refused rather than taken for another.
/// </summary>
internal static class QueueMessages
{
    /// <summary>
    /// The longest line either side reads: a request is a link at most, and a reply one download.
    /// </summary>
    public const int MaxLineLength = 16 * 1024;

    public static string Write(QueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.Serialize(new QueueRequestMessage(request.Kind.ToString(), request.Link?.OriginalString, request.Job?.ToString()));
    }

    public static string Write(QueueReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        return JsonSerializer.Serialize(new QueueReplyMessage(
            reply.Outcome.ToString(),
            reply.Problem,
            reply.Job is { } job ? Write(job) : null,
            reply.LibraryPath,
            reply.AlreadyQueued,
            reply.AlreadySaved));
    }

    /// <exception cref="InvalidDataException">The line isn't a queue request.</exception>
    public static QueueRequest ReadRequest(string line)
    {
        try
        {
            QueueRequestMessage message = JsonSerializer.Deserialize<QueueRequestMessage>(line)
                ?? throw new InvalidDataException("A queue request is empty.");
            return new QueueRequest(
                Parse<QueueRequestKind>(message.Kind),
                message.Link is { } link ? new Uri(link, UriKind.Absolute) : null,
                message.Job is { } job ? new DownloadJobId(Guid.Parse(job, CultureInfo.InvariantCulture)) : null);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or UriFormatException)
        {
            throw new InvalidDataException("A queue request couldn't be read.", exception);
        }
    }

    /// <exception cref="InvalidDataException">The line isn't a queue reply.</exception>
    public static QueueReply ReadReply(string line)
    {
        try
        {
            QueueReplyMessage message = JsonSerializer.Deserialize<QueueReplyMessage>(line)
                ?? throw new InvalidDataException("A queue reply is empty.");
            return new QueueReply(
                Parse<QueueOutcome>(message.Outcome),
                message.Problem,
                message.Job is { } job ? Read(job) : null,
                message.LibraryPath,
                message.AlreadyQueued,
                message.AlreadySaved);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or UriFormatException)
        {
            throw new InvalidDataException("A queue reply couldn't be read.", exception);
        }
    }

    private static QueueJobMessage Write(DownloadJobSnapshot job)
        => new(
            job.Id.ToString(),
            job.File.Mod.Game.Source.Value,
            job.File.Mod.Game.Key,
            job.File.Mod.Id,
            job.File.Id,
            job.TargetDirectory,
            job.FileName,
            job.Status.ToString(),
            job.BytesDownloaded,
            job.TotalBytes,
            job.FailureReason?.ToString(),
            job.CreatedAt,
            job.HandoffPage?.AbsoluteUri);

    private static DownloadJobSnapshot Read(QueueJobMessage job)
        => new(
            new DownloadJobId(Guid.Parse(job.Id, CultureInfo.InvariantCulture)),
            new ModFileRef(new ModRef(new GameRef(new SourceId(job.Source), job.Game), job.Mod), job.File),
            job.TargetDirectory,
            job.FileName,
            Parse<DownloadJobStatus>(job.Status),
            job.BytesDownloaded,
            job.TotalBytes,
            job.FailureReason is { } reason ? Parse<DownloadFailureReason>(reason) : null,
            job.CreatedAt,
            job.HandoffPage is { } page ? new Uri(page, UriKind.Absolute) : null);

    /// <summary>
    /// An enum's value by its name only: <see cref="Enum.Parse{TEnum}(string)"/> also takes numbers, and numbers that
    /// aren't values.
    /// </summary>
    private static TEnum Parse<TEnum>(string name)
        where TEnum : struct, Enum
        => Enum.GetNames<TEnum>().Contains(name, StringComparer.Ordinal)
            ? Enum.Parse<TEnum>(name)
            : throw new FormatException($"'{name}' isn't a {typeof(TEnum).Name}.");
}
