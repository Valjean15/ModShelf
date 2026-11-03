using System.ComponentModel;

using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP tools for downloads: the queue, downloading a file into the library, and pausing, resuming, retrying or
/// cancelling a download. The desktop app runs the queue when it runs; otherwise this server does (<see cref="McpQueue"/>).
/// </summary>
[McpServerToolType]
internal sealed partial class DownloadTools(McpQueue queue, IDownloadService downloads, ILogger<DownloadTools> logger)
{
    private const string DesktopApp = "desktop app";
    private const string ThisServer = "this MCP server";

    private const string IdDescription = "The download's ID, from downloads_list, or its first 8 characters or more.";

    // A download's ID, or enough of its start to tell it from the others, as the CLI's queue list shows them.
    private const int ShortestId = 8;

    // The most links one call to download takes.
    private const int MostLinks = 50;

    // The code of a call to download with the old link, in the result and the log's warning.
    private const string LinkArgumentCode = "DL_LINK_ARG";

    [McpServerTool(Name = "downloads_list", Title = "List the downloads", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the downloads in the queue, the oldest first: each one's status, progress and where the file goes. For one waiting for the user to start it on the site, the page to open, while this server runs the downloads; the desktop app, when it runs them, shows the user that page itself.")]
    public Task<DownloadListing> ListAsync(CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            // While this server runs the queue, it knows the pages of the downloads waiting for the user; the database doesn't.
            IReadOnlyList<DownloadJobSnapshot> jobs = await queue.ListHereAsync(cancellationToken).ConfigureAwait(false)
                ?? await downloads.ListAsync(cancellationToken).ConfigureAwait(false);
            return new DownloadListing([.. jobs.Select(McpEntries.Download)]);
        });

    [McpServerTool(Name = "download", Title = "Download into the library", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Downloads mods' files into the library, where library.folders says, as ModShelf's links do: a file's link, such as an update's, gets that file; a mod's link, its main file. Give one link, or several, up to 50: they're downloaded in the order given, and one that can't be still lets the others go on, with its problem said in its result. A file the library has already isn't downloaded again, and an unfinished download of it goes on. Nothing is installed. With a free Nexus Mods account, the user starts each download on the site: the download waits, with the page to open.")]
    public Task<DownloadsResult> DownloadAsync(
        McpServer server,
        [Description("Links to files or mods, one or more, up to 50, such as updates' links from library_updates, files' from mod_show, or nxm:// links the site handed over. (link, a single link, still works but is deprecated.)")] string[]? links = null,
        [Description("Deprecated: one link, as download took before 0.9.3. Pass links, a list, instead.")] string? link = null,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            DeprecatedArgument? deprecated = null;
            if (link is not null)
            {
                if (links is not null)
                {
                    throw new McpException("Pass only 'links'; 'link' is the old name for a single link.");
                }

                links = [link];
                deprecated = new DeprecatedArgument(LinkArgumentCode, "link", "links", new DownloadLinks(links));
                LogLegacyLink(logger, LinkArgumentCode, links.Length, server?.ClientInfo?.Name ?? "unknown");
            }

            if (links is not { Length: > 0 })
            {
                throw new McpException("'links' needs at least one link.");
            }

            if (links.Length > MostLinks)
            {
                throw new McpException($"Give from 1 to {MostLinks} links.");
            }

            List<LinkDownloadResult> results = [];
            foreach (string each in links)
            {
                results.Add(await DownloadOneAsync(each, cancellationToken).ConfigureAwait(false));
            }

            results = await AsTheyAreNowAsync(results, cancellationToken).ConfigureAwait(false);
            string message = Summarize(results);
            return new DownloadsResult(
                results,
                deprecated,
                deprecated is null
                    ? message
                    : "Note: 'link' is the old way to call download and will stop working; pass 'links' as a list instead, as in "
                        + $"the example. {message}");
        });

    [McpServerTool(Name = "downloads_pause", Title = "Pause a download", Idempotent = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Pauses a download that's queued or under way. It keeps what it has downloaded, and goes on when resumed.")]
    public Task<DownloadResult> PauseAsync([Description(IdDescription)] string id, CancellationToken cancellationToken)
        => ChangeAsync(QueueRequest.Pause, id, "Paused.", cancellationToken);

    [McpServerTool(Name = "downloads_resume", Title = "Resume a download", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Resumes a paused download, from where it stopped.")]
    public Task<DownloadResult> ResumeAsync([Description(IdDescription)] string id, CancellationToken cancellationToken)
        => ChangeAsync(QueueRequest.Resume, id, "Resumed.", cancellationToken);

    [McpServerTool(Name = "downloads_retry", Title = "Retry a download", Idempotent = false, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Tries a failed download again.")]
    public Task<DownloadResult> RetryAsync([Description(IdDescription)] string id, CancellationToken cancellationToken)
        => ChangeAsync(QueueRequest.Retry, id, "Trying again.", cancellationToken);

    [McpServerTool(Name = "downloads_cancel", Title = "Cancel a download", Idempotent = true, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Cancels a download that isn't finished, and deletes the part of the file it downloaded. A finished file is never deleted.")]
    public Task<DownloadResult> CancelAsync([Description(IdDescription)] string id, CancellationToken cancellationToken)
        => ChangeAsync(QueueRequest.Cancel, id, "Cancelled.", cancellationToken);

    /// <summary>
    /// The one link's message, or such as "Of 3 links, 2 are downloading or waiting for the user, and 1 couldn't be
    /// downloaded; each result says more, in the order the links were given.".
    /// </summary>
    private static string Summarize(List<LinkDownloadResult> results)
    {
        int refused = results.Count(result => result.Problem is not null);
        int saved = results.Count(result => result.AlreadySaved);
        int done = results.Count(result => result.Download?.Status == nameof(DownloadJobStatus.Completed));
        int failed = results.Count(result => result.Download?.Status == nameof(DownloadJobStatus.Failed));
        int going = results.Count - refused - saved - done - failed;
        if (results.Count == 1)
        {
            return results[0].Message;
        }

        List<string> parts = [];
        if (going > 0)
        {
            parts.Add(going == 1 ? "1 is downloading or waiting for the user" : $"{going} are downloading or waiting for the user");
        }

        if (done > 0)
        {
            parts.Add(done == 1 ? "1 is downloaded" : $"{done} are downloaded");
        }

        if (failed > 0)
        {
            parts.Add($"{failed} failed");
        }

        if (saved > 0)
        {
            parts.Add(saved == 1 ? "1 was in the library already" : $"{saved} were in the library already");
        }

        if (refused > 0)
        {
            parts.Add($"{refused} couldn't be downloaded");
        }

        string said = parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])}, and {parts[^1]}";
        return $"Of {results.Count} links, {said}; each result says more, in the order the links were given.";
    }

    /// <summary>
    /// What to say of a link that couldn't be downloaded; null for a failure that isn't the link's, the site's or the
    /// queue's, which fails the call.
    /// </summary>
    private static string? ProblemOf(Exception exception) => exception is McpException refused ? refused.Message : McpProblems.Describe(exception);

    /// <summary>
    /// Downloads one link's file into the library. A problem with the link, the site or the queue is its result's
    /// problem, so the other links go on.
    /// </summary>
    private async Task<LinkDownloadResult> DownloadOneAsync(string link, CancellationToken cancellationToken)
    {
        QueueReply reply;
        bool ranHere;
        try
        {
            (reply, ranHere) = await SendAsync(QueueRequest.Download(McpProblems.Link(link)), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ProblemOf(exception) is { } problem)
        {
            return new LinkDownloadResult(null, null, false, false, null, problem, problem);
        }

        DownloadEntry? job = reply.Job is { } snapshot ? McpEntries.Download(snapshot) : null;
        return Described(new LinkDownloadResult(
            job, reply.LibraryPath, reply.AlreadySaved, reply.AlreadyQueued, ranHere ? ThisServer : DesktopApp, null, string.Empty));
    }

    /// <summary>
    /// The results with their downloads as the queue has them now, rather than as each request left them: a small file
    /// may be downloaded, or a download failed, while the next links were asked about. When the queue can't be read, the
    /// results stay as they are.
    /// </summary>
    private async Task<List<LinkDownloadResult>> AsTheyAreNowAsync(List<LinkDownloadResult> results, CancellationToken cancellationToken)
    {
        if (results.All(result => result.Download is null))
        {
            return results;
        }

        IReadOnlyList<DownloadJobSnapshot> jobs;
        try
        {
            jobs = await queue.ListHereAsync(cancellationToken).ConfigureAwait(false)
                ?? await downloads.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The downloads were asked for already: what each request left still holds.
            return results;
        }

        Dictionary<string, DownloadEntry> now = [];
        foreach (DownloadJobSnapshot job in jobs)
        {
            now[job.Id.ToString()] = McpEntries.Download(job);
        }

        return [.. results.Select(result => result.Download is { } asked && now.TryGetValue(asked.Id, out DownloadEntry? current)
            ? Described(result with { Download = current })
            : result)];
    }

    /// <summary>
    /// The result, with what happened to the link and what to expect said as its download is.
    /// </summary>
    private static LinkDownloadResult Described(LinkDownloadResult result)
    {
        string message = result switch
        {
            { AlreadySaved: true } => "The library has this file already, so nothing downloads.",
            { Download: { Status: nameof(DownloadJobStatus.Completed) }, LibraryPath: null } => "The file is downloaded, outside the library, at the download's path.",
            { Download.Status: nameof(DownloadJobStatus.Completed) } =>
                "The file is downloaded into the library. library_changes says what became of the version it replaced, as "
                + "library.oldVersions says.",
            { Download: { Status: nameof(DownloadJobStatus.Failed), Failure: var failure } } =>
                $"The download failed ({failure}). downloads_retry tries it again.",
            { Download.Status: nameof(DownloadJobStatus.Cancelled) } => "The download was cancelled.",
            { Download.Status: nameof(DownloadJobStatus.Paused) } => "The download is paused. downloads_resume goes on with it.",
            { Download.Status: nameof(DownloadJobStatus.AwaitingHandoff) } =>
                "The site needs the user to start this download on its website: the download's handoffPage is where, with "
                + "its mod manager download. ModShelf's desktop app gets the link the site hands over, and the download goes on.",
            { AlreadyQueued: true } => "A download of this file was in the queue already, and goes on.",
            { LibraryPath: null } => "A download of this file to a folder outside the library goes on with this link.",
            _ => "The file is downloading into the library. downloads_list shows how it goes, and the page to open when the "
                + "site needs the user to start it there.",
        };
        if (result is
            {
                RunBy: ThisServer,
                Download.Status: nameof(DownloadJobStatus.Queued) or nameof(DownloadJobStatus.Resolving)
                    or nameof(DownloadJobStatus.Downloading) or nameof(DownloadJobStatus.Verifying),
            })
        {
            message += " No desktop app is running, so this MCP server downloads it, until no download is under way; closing "
                + "the MCP server pauses what's left, which goes on the next time ModShelf runs its downloads.";
        }

        return result with { Message = message };
    }

    private Task<DownloadResult> ChangeAsync(Func<DownloadJobId, QueueRequest> request, string id, string done, CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            DownloadJobId job = await FindAsync(id, cancellationToken).ConfigureAwait(false);
            (QueueReply reply, bool ranHere) = await SendAsync(request(job), cancellationToken).ConfigureAwait(false);
            return new DownloadResult(
                reply.Job is { } snapshot ? McpEntries.Download(snapshot) : null,
                null,
                false,
                false,
                ranHere ? ThisServer : DesktopApp,
                done);
        });

    /// <exception cref="McpException">The request wasn't carried out, or the desktop app didn't reply.</exception>
    private async Task<(QueueReply Reply, bool RanHere)> SendAsync(QueueRequest request, CancellationToken cancellationToken)
    {
        (QueueReply Reply, bool RanHere) sent;
        try
        {
            sent = await queue.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new McpException(
                "ModShelf's desktop app took the request, but didn't reply in time; it may have carried it out. downloads_list shows the queue.",
                exception);
        }
        catch (InvalidDataException exception)
        {
            throw new McpException(
                "The ModShelf desktop app that's running replied in a way this MCP server can't read, as a newer or older one "
                + "does. Restart the MCP server once both are the same ModShelf.",
                exception);
        }

        return sent.Reply.Outcome == QueueOutcome.Done
            ? sent
            : throw new McpException(sent.Reply.Problem ?? $"The request wasn't carried out: {sent.Reply.Outcome}.");
    }

    /// <summary>
    /// The download with <paramref name="id"/>, or the only one whose ID starts with it.
    /// </summary>
    /// <exception cref="McpException">There's no such download, or more than one.</exception>
    private async Task<DownloadJobId> FindAsync(string id, CancellationToken cancellationToken)
    {
        string text = id?.Trim() ?? string.Empty;
        if (Guid.TryParse(text, out Guid full) && full != Guid.Empty)
        {
            return new DownloadJobId(full);
        }

        if (text.Length < ShortestId)
        {
            throw new McpException($"A download's ID is its full ID, from downloads_list, or its first {ShortestId} characters or more.");
        }

        IReadOnlyList<DownloadJobSnapshot> jobs = await downloads.ListAsync(cancellationToken).ConfigureAwait(false);
        DownloadJobSnapshot[] matching = [.. jobs.Where(job => job.Id.ToString().StartsWith(text, StringComparison.OrdinalIgnoreCase))];
        return matching.Length switch
        {
            1 => matching[0].Id,
            0 => throw new McpException($"No download has an ID that starts with {text}."),
            _ => throw new McpException($"More than one download's ID starts with {text}; give more of it."),
        };
    }
}
