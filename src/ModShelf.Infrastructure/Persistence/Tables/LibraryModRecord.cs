using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// A row of the <c>LibraryMods</c> table, with its files. The repository maps it to and from <see cref="LibraryMod"/>.
/// </summary>
/// <remarks>A mutable class rather than a record, because EF Core tracks entities by reference.</remarks>
internal sealed class LibraryModRecord
{
    /// <summary>
    /// The row's key, assigned by SQLite in insertion order. It gives the library a stable order.
    /// </summary>
    public long Sequence { get; set; }

    public required string SourceId { get; set; }

    public required string GameKey { get; set; }

    public required string ModId { get; set; }

    public required string Name { get; set; }

    public string? Author { get; set; }

    public string? PageUrl { get; set; }

    public bool IsWatched { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    public List<LibraryFileRecord> Files { get; set; } = [];

    public static LibraryModRecord From(LibraryMod mod)
    {
        LibraryModRecord record = new()
        {
            SourceId = mod.Mod.Game.Source.Value,
            GameKey = mod.Mod.Game.Key,
            ModId = mod.Mod.Id,
            Name = mod.Name,
        };
        record.CopyFrom(mod);
        return record;
    }

    /// <summary>
    /// Takes over everything but the key from <paramref name="mod"/>. Files are matched by their ID, so a file that
    /// stays keeps its row and its place.
    /// </summary>
    public void CopyFrom(LibraryMod mod)
    {
        Name = mod.Name;
        Author = mod.Author;
        PageUrl = mod.PageUrl?.AbsoluteUri;
        IsWatched = mod.IsWatched;
        AddedAt = mod.AddedAt;
        CheckedAt = mod.CheckedAt;

        Files.RemoveAll(record => mod.Files.All(file => file.File.Id != record.FileId));
        foreach (LibraryFile file in mod.Files)
        {
            if (Files.Find(record => record.FileId == file.File.Id) is { } stored)
            {
                stored.CopyFrom(file);
            }
            else
            {
                Files.Add(LibraryFileRecord.From(file));
            }
        }
    }

    /// <summary>
    /// Rebuilds the mod this row and its files store.
    /// </summary>
    /// <exception cref="InvalidDataException">The rows hold values no library mod can have.</exception>
    public LibraryMod ToMod()
    {
        try
        {
            ModRef mod = new(new GameRef(new SourceId(SourceId), GameKey), ModId);
            return LibraryMod.Restore(
                mod,
                Name,
                Author,
                PageUrl is null ? null : new Uri(PageUrl, UriKind.Absolute),
                IsWatched,
                AddedAt,
                CheckedAt,
                Files.OrderBy(file => file.Sequence).Select(file => file.ToFile(mod)));
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            throw new InvalidDataException(
                $"Mod {ModId} of {GameKey} on {SourceId} in the library database can't be loaded: {exception.Message}", exception);
        }
    }
}
