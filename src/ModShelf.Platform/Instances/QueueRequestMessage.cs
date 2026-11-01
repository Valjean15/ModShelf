namespace ModShelf.Platform.Instances;

/// <summary>
/// A queue request as a line of JSON on the queue's pipe.
/// </summary>
internal sealed record QueueRequestMessage(string Kind, string? Link, string? Job);
