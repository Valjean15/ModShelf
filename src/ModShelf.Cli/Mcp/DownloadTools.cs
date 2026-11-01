using System.ComponentModel;

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
internal sealed class DownloadTools(McpQueue queue, IDownloadService downloads)
{
    private const string DesktopApp = "desktop app";
    private const string ThisServer = "this MCP server";

    private const string IdDescription = "The download's ID, from downloads_list, or its first 8 characters or more.";

    // A download's ID, or enough of its start to tell it from the others, as the CLI's queue list shows them.
    private const int ShortestId = 8;

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
    [Description("Downloads a mod's file into the library, where library.folders says, as ModShelf's links do: a file's link, such as an update's, gets that file; a mod's link, its main file. A file the library has already isn't downloaded again, and an unfinished download of it goes on. Nothing is installed. With a free Nexus Mods account, the user starts each download on the site: the download waits, with the page to open.")]
    public Task<DownloadResult> DownloadAsync(
        [Description("A link to the file or the mod, such as an update's link from library_updates, a file's from mod_show, or an nxm:// link the site handed over.")] string link,
        CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            (QueueReply reply, bool ranHere) = await SendAsync(QueueRequest.Download(McpProblems.Link(link)), cancellationToken).ConfigureAwait(false);
            DownloadEntry? job = reply.Job is { } snapshot ? McpEntries.Download(snapshot) : null;
            string message = (reply, job) switch
            {
                ({ AlreadySaved: true }, _) => "The library has this file already, so nothing downloads.",
                (_, { Status: nameof(DownloadJobStatus.AwaitingHandoff) }) =>
                    "The site needs the user to start this download on its website: the download's handoffPage is where, with "
                    + "its mod manager download. ModShelf's desktop app gets the link the site hands over, and the download goes on.",
                ({ AlreadyQueued: true }, _) => "A download of this file was in the queue already, and goes on.",
                _ when reply.LibraryPath is null => "A download of this file to a folder outside the library goes on with this link.",
                _ => "The file is downloading into the library. downloads_list shows how it goes, and the page to open when the "
                    + "site needs the user to start it there.",
            };
            if (ranHere && job is { Status: not nameof(DownloadJobStatus.AwaitingHandoff) })
            {
                message += " No desktop app is running, so this MCP server downloads it, until no download is under way; closing "
                    + "the MCP server pauses what's left, which goes on the next time ModShelf runs its downloads.";
            }

            return new DownloadResult(job, reply.LibraryPath, reply.AlreadySaved, reply.AlreadyQueued, ranHere ? ThisServer : DesktopApp, message);
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
