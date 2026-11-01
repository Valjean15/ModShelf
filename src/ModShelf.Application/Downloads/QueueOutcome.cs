namespace ModShelf.Application.Downloads;

/// <summary>
/// How a <see cref="QueueRequest"/> went.
/// </summary>
public enum QueueOutcome
{
    Done,

    /// <summary>
    /// No download has the request's ID.
    /// </summary>
    NotFound,

    /// <summary>
    /// The download's status doesn't allow it, such as pausing one that's finished.
    /// </summary>
    NotAllowed,

    /// <summary>
    /// A problem the user can act on, which <see cref="QueueReply.Problem"/> says: such as a link no site knows, an
    /// account to link, a site that can't be reached, or another process running the queue.
    /// </summary>
    Refused,

    /// <summary>
    /// Something ModShelf didn't expect; the log of the process that runs the queue says what.
    /// </summary>
    Failed,
}
