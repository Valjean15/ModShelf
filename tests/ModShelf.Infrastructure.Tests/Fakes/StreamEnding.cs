namespace ModShelf.Infrastructure.Tests.Fakes;

/// <summary>
/// What a <see cref="ScriptedStream"/> does once it has sent its data.
/// </summary>
internal enum StreamEnding
{
    /// <summary>The body ends normally.</summary>
    End,

    /// <summary>The connection resets with an <see cref="IOException"/>.</summary>
    Drop,

    /// <summary>No more data arrives until the read is cancelled.</summary>
    Stall,
}
