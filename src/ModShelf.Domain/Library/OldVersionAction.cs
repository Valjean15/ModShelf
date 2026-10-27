namespace ModShelf.Domain.Library;

/// <summary>
/// What an <see cref="OldVersionRule"/> does with a library file's old versions.
/// </summary>
public enum OldVersionAction
{
    /// <summary>Keeps every version.</summary>
    KeepAll,

    /// <summary>Moves the old versions into an <c>_archive</c> folder, where they stay in the library.</summary>
    Archive,

    /// <summary>Keeps the newest versions, and deletes the others from the disk and the library.</summary>
    KeepLatest,
}
