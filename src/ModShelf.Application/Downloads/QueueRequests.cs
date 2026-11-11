using System.Net;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Links;
using ModShelf.Domain.Downloads;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Downloads;

/// <summary>
/// Carries out queue requests on this process's queue: downloads through <see cref="IOpenedLinks"/>, as a link the
/// system hands over, and the rest through <see cref="IDownloadService"/>. A new download's reply waits a little for it
/// to start, so that it says how it went: downloading, or waiting for the user to start it on the site, with the page.
/// </summary>
internal sealed partial class QueueRequests(IOpenedLinks links, IDownloadService downloads, TimeProvider time, ILogger<QueueRequests> logger)
    : IQueueRequests
{
    /// <summary>
    /// How long a download's reply waits for it to start, at most; a queue busy with others doesn't start it as soon.
    /// </summary>
    internal static readonly TimeSpan StartWait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How often the reply looks whether the download has started.
    /// </summary>
    internal static readonly TimeSpan LookEvery = TimeSpan.FromMilliseconds(250);

    public async Task<QueueReply> HandleAsync(QueueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        LogRequest(logger, request.Kind);
        try
        {
            if (request.Kind == QueueRequestKind.Download)
            {
                Uri link = request.Link ?? throw new ArgumentException("A download needs a link.", nameof(request));
                OpenedLink opened = await links.OpenAsync(link, cancellationToken).ConfigureAwait(false);
                return new QueueReply(
                    QueueOutcome.Done,
                    Job: opened.Download?.Job is { } queued ? await StartedAsync(queued, cancellationToken).ConfigureAwait(false) : null,
                    LibraryPath: opened.LibraryPath,
                    AlreadyQueued: opened.Download?.AlreadyQueued == true,
                    AlreadySaved: opened.Download is null || opened.Download.AlreadySaved);
            }

            DownloadJobId job = request.Job ?? throw new ArgumentException("The request needs a download's ID.", nameof(request));
            if (request.Kind == QueueRequestKind.TakeFile)
            {
                return await TakeFileAsync(job, request.Path ?? throw new ArgumentException("Handing a download a file needs its path.", nameof(request)), cancellationToken)
                    .ConfigureAwait(false);
            }

            Task change = request.Kind switch
            {
                QueueRequestKind.Pause => downloads.PauseAsync(job, cancellationToken),
                QueueRequestKind.Resume => downloads.ResumeAsync(job, cancellationToken),
                QueueRequestKind.Retry => downloads.RetryAsync(job, cancellationToken),
                QueueRequestKind.Cancel => downloads.CancelAsync(job, cancellationToken),
                _ => throw new ArgumentException($"There's no queue request of kind {request.Kind}.", nameof(request)),
            };
            await change.ConfigureAwait(false);
            IReadOnlyList<DownloadJobSnapshot> jobs = await downloads.ListAsync(cancellationToken).ConfigureAwait(false);
            return new QueueReply(QueueOutcome.Done, Job: jobs.FirstOrDefault(snapshot => snapshot.Id == job));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            return new QueueReply(QueueOutcome.NotFound, $"No download has the ID {request.Job}.");
        }
        catch (DownloadQueueBusyException exception)
        {
            return new QueueReply(QueueOutcome.Refused, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new QueueReply(QueueOutcome.NotAllowed, exception.Message);
        }
        catch (AuthenticationFailedException)
        {
            return new QueueReply(QueueOutcome.Refused, "The site needs a linked account for that, or refused the one linked. Link one in ModShelf, then try again.");
        }
        catch (HttpRequestException exception)
        {
            return new QueueReply(QueueOutcome.Refused, exception.StatusCode switch
            {
                HttpStatusCode.NotFound or HttpStatusCode.Gone => "The site has no such mod or file.",
                HttpStatusCode.TooManyRequests => "The site asked ModShelf to slow down. Try again in a while.",
                _ => $"The site couldn't be asked: {exception.Message}",
            });
        }
        catch (Exception exception) when (exception is UnsupportedLinkException or ArgumentException or IOException or InvalidDataException)
        {
            return new QueueReply(QueueOutcome.Refused, exception.Message);
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception, request.Kind);
            return new QueueReply(QueueOutcome.Failed, "Something went wrong. ModShelf's log says what.");
        }
    }

    /// <summary>
    /// Hands the download the file, and says how it went: in words for the user, unless it was taken.
    /// </summary>
    private async Task<QueueReply> TakeFileAsync(DownloadJobId job, string path, CancellationToken cancellationToken)
    {
        TakeFileOutcome outcome = await downloads.TakeFileAsync(job, path, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DownloadJobSnapshot> jobs = await downloads.ListAsync(cancellationToken).ConfigureAwait(false);
        DownloadJobSnapshot? now = jobs.FirstOrDefault(snapshot => snapshot.Id == job);
        return outcome switch
        {
            TakeFileOutcome.Taken => new QueueReply(QueueOutcome.Done, Job: now),
            TakeFileOutcome.HashMismatch => new QueueReply(
                QueueOutcome.Refused,
                "That file isn't the download's: its checksum isn't the one the site gives. It's left where it was, and the download still waits.",
                now),
            TakeFileOutcome.NotFound => new QueueReply(QueueOutcome.Refused, "There's no file at that path. The download still waits.", now),
            _ => new QueueReply(
                QueueOutcome.Refused,
                "The file couldn't be moved into place, such as because another program has it open. It's left where it was, and ModShelf's log says more.",
                now),
        };
    }

    /// <summary>
    /// The download once it has started, or as it is after <see cref="StartWait"/>, or once it's gone from the queue.
    /// </summary>
    private async Task<DownloadJobSnapshot> StartedAsync(DownloadJobSnapshot job, CancellationToken cancellationToken)
    {
        DownloadJobSnapshot current = job;
        DateTimeOffset until = time.GetUtcNow() + StartWait;
        using PeriodicTimer looking = new(LookEvery, time);
        while (current.Status is DownloadJobStatus.Queued or DownloadJobStatus.Resolving
            && time.GetUtcNow() < until
            && await looking.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            IReadOnlyList<DownloadJobSnapshot> jobs = await downloads.ListAsync(cancellationToken).ConfigureAwait(false);
            if (jobs.FirstOrDefault(snapshot => snapshot.Id == job.Id) is not { } now)
            {
                break;
            }

            current = now;
        }

        return current;
    }
}
