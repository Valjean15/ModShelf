using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Presentation.Common;

/// <summary>
/// What counts as a site's problem, and how the screens tell the user of one.
/// </summary>
internal static class SiteProblems
{
    /// <summary>
    /// Whether the site couldn't do what was asked: it refused the account, answered with an error, or couldn't be
    /// reached in time. Without a linked account, or offline, that's expected, so it's told or logged without a stack
    /// trace.
    /// </summary>
    public static bool IsSiteProblem(Exception exception)
        => exception is AuthenticationFailedException or HttpRequestException || IsTimeout(exception);

    /// <summary>
    /// Whether a request ran past its client's timeout, which .NET reports as cancelled.
    /// </summary>
    public static bool IsTimeout(Exception exception) => exception is TaskCanceledException { InnerException: TimeoutException };

    /// <summary>
    /// What to tell the user of a site's problem, naming the site; null for another problem.
    /// </summary>
    /// <param name="failed">What to say of any problem but the account's, such as "{0} couldn't search: {1}", with the
    /// site's name and what went wrong.</param>
    public static string? Describe(Exception exception, string site, string failed) => exception switch
    {
        AuthenticationFailedException => Text.Format(Strings.BrowseAccountNeeded, site),
        _ when IsSiteProblem(exception) => Text.Format(failed, site, exception.Message),
        _ => null,
    };
}
