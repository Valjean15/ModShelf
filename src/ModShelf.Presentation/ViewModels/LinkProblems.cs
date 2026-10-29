using System.Net;

using ModShelf.Application.Downloads;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// What to tell the user when a link couldn't be added or handed over, as the CLI tells it.
/// </summary>
internal static class LinkProblems
{
    /// <param name="library">Whether the link's file was to go into the library.</param>
    /// <returns>The message; or null for an error that isn't the user's to act on, which belongs in the log.</returns>
    public static string? Describe(Exception exception, bool library) => exception switch
    {
        DownloadQueueBusyException => Strings.DownloadsQueueBusyRefused,
        // The library wraps the refusal of every source; its own refusal is of a link to a site without a catalog.
        UnsupportedLinkException { InnerException: not UnsupportedLinkException } when library => Strings.DownloadsOnlyModSites,
        UnsupportedLinkException => Strings.DownloadsNoSource,
        IOException when library => Text.Format(Strings.DownloadsLibraryPlaceProblem, exception.Message),
        InvalidDataException => exception.Message,
        ArgumentException refused => MessageForUser(refused),
        AuthenticationFailedException => Strings.LinkNeedsAccount,
        HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone } => Strings.LinkNotFound,
        HttpRequestException failed => Text.Format(Strings.LinkLookUpFailed, failed.Message),
        _ => null,
    };

    /// <summary>
    /// The exception's message without the "(Parameter 'name')" .NET appends, which means nothing to the user.
    /// </summary>
    internal static string MessageForUser(ArgumentException exception)
        => exception.ParamName is null
            ? exception.Message
            : exception.Message.Replace($" (Parameter '{exception.ParamName}')", string.Empty, StringComparison.Ordinal);
}
