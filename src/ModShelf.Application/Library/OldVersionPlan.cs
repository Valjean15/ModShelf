using ModShelf.Domain.Library;

namespace ModShelf.Application.Library;

/// <summary>
/// What an <see cref="Domain.Library.OldVersionRule"/> does with a library file's old versions.
/// </summary>
/// <param name="Archive">The old versions to move into an <c>_archive</c> folder, as they are now.</param>
/// <param name="Delete">The old versions to delete, as they are now.</param>
internal sealed record OldVersionPlan(IReadOnlyList<LibraryFile> Archive, IReadOnlyList<LibraryFile> Delete)
{
    public static OldVersionPlan None { get; } = new([], []);
}
