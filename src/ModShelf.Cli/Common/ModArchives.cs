using ModShelf.Application.Contents;
using ModShelf.Domain.Library;

namespace ModShelf.Cli.Common;

/// <summary>
/// A library mod's archives whose contents can be shown, and the one a name picks, for <c>library contents</c> and the
/// MCP server's <c>library_contents</c> (ADR 0106).
/// </summary>
internal static class ModArchives
{
    /// <summary>
    /// The mod's archives, .zip, .7z or .rar, the newest first.
    /// </summary>
    public static LibraryFile[] Of(LibraryMod mod, ILibraryContents contents)
        => [.. mod.Files.Where(file => contents.CanList(file.FileName)).OrderByDescending(file => file.DownloadedAt)];

    /// <summary>
    /// The archive <paramref name="name"/> names, by its file name or its ID on its site, in any case; or, without a
    /// name, the only one. Null for none, or for more than one without a name.
    /// </summary>
    public static LibraryFile? Pick(IReadOnlyList<LibraryFile> archives, string? name)
    {
        if (name?.Trim() is not { Length: > 0 } wanted)
        {
            return archives.Count == 1 ? archives[0] : null;
        }

        return archives.FirstOrDefault(file => string.Equals(file.FileName, wanted, StringComparison.OrdinalIgnoreCase))
            ?? archives.FirstOrDefault(file => string.Equals(file.File.Id, wanted, StringComparison.OrdinalIgnoreCase));
    }
}
