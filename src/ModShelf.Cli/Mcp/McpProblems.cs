using System.Net;

using ModelContextProtocol;

using ModShelf.Application.Downloads;
using ModShelf.Cli.Commands;
using ModShelf.Infrastructure.Persistence;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// What to tell the agent when a tool's work went wrong in a way the user or the agent can act on: as the tool's error,
/// an <see cref="McpException"/>. Anything else is ModShelf's, and the MCP server says only that the tool failed, with
/// the details in the log.
/// </summary>
internal static class McpProblems
{
    /// <summary>
    /// What to say when another process runs this library's downloads, but not the desktop app that takes requests.
    /// </summary>
    public const string QueueBusyElsewhere =
        "Another ModShelf process runs this library's downloads, such as a download command of the CLI, or a desktop app "
        + "from before 0.9.1. Try again once it has finished, or update the desktop app.";

    /// <summary>
    /// Runs a tool's work, and turns a problem with it into the tool's error.
    /// </summary>
    /// <exception cref="McpException">The work went wrong in a way someone can act on.</exception>
    public static async Task<T> GuardAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        try
        {
            return await work().ConfigureAwait(false);
        }
        catch (Exception exception) when (Describe(exception) is { } problem)
        {
            throw new McpException(problem, exception);
        }
    }

    /// <summary>
    /// A link the agent gave, as a complete link.
    /// </summary>
    /// <exception cref="McpException">It isn't one.</exception>
    public static Uri Link(string link)
        => Uri.TryCreate(link?.Trim(), UriKind.Absolute, out Uri? parsed)
            ? parsed
            : throw new McpException("That isn't a complete link, such as https://www.nexusmods.com/stardewvalley/mods/2400.");

    /// <returns>What to say; null for a problem that isn't for the user or the agent to act on.</returns>
    public static string? Describe(Exception exception) => exception switch
    {
        McpException => null,
        DownloadQueueBusyException => QueueBusyElsewhere,
        UnsupportedLinkException refused => refused.Message,
        ArgumentException refused => refused.MessageForUser(),
        AuthenticationFailedException =>
            "The site needs a linked account for that, or refused the one linked. The user links accounts in ModShelf's "
            + "desktop app, on its Settings screen, or with the CLI's account link; the MCP server doesn't.",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone } => "The site has no such mod or file.",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "The site asked ModShelf to slow down. Try again in a while.",
        HttpRequestException failed => $"The site couldn't be asked: {failed.Message}",
        LibraryDatabaseUnavailableException unavailable => unavailable.Message,
        InvalidDataException unreadable => unreadable.Message,
        IOException or UnauthorizedAccessException => exception.Message,
        _ => null,
    };
}
