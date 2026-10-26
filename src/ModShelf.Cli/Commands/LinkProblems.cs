using System.Net;

using ModShelf.Application.Downloads;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Commands;

/// <summary>
/// What to tell the user when looking a link up went wrong, for the commands that take links.
/// </summary>
internal static class LinkProblems
{
    /// <param name="unsupported">What to say when no source can handle the link; null for the exception's own message.</param>
    /// <returns>The message and exit code, or null for an error that isn't the user's to act on.</returns>
    public static (string Message, int ExitCode)? Describe(Exception exception, string? unsupported = null) => exception switch
    {
        UnsupportedLinkException refused => (unsupported ?? refused.Message, ExitCodes.BadInput),
        ArgumentException refused => (refused.MessageForUser(), ExitCodes.BadInput),
        AuthenticationFailedException => (
            "The site needs a linked account to look that link up, or refused the one linked. Link it with account link, "
            + "then run the same command again.",
            ExitCodes.Failed),
        HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone } => (
            "The site has no such mod or file.",
            ExitCodes.BadInput),
        HttpRequestException failed => ($"The site couldn't look that link up: {failed.Message}", ExitCodes.Failed),
        _ => null,
    };
}
