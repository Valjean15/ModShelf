namespace ModShelf.Platform.Instances;

/// <summary>
/// A queue reply as a line of JSON on the queue's pipe.
/// </summary>
internal sealed record QueueReplyMessage(
    string Outcome,
    string? Problem,
    QueueJobMessage? Job,
    string? LibraryPath,
    bool AlreadyQueued,
    bool AlreadySaved);
