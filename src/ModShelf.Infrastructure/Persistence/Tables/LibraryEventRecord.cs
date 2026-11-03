using ModShelf.Application.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// A row of the library's history: something that happened to a mod or one of its files.
/// </summary>
internal sealed class LibraryEventRecord
{
    public long Sequence { get; set; }

    public DateTimeOffset At { get; set; }

    public LibraryEventKind Kind { get; set; }

    public required string SourceId { get; set; }

    public required string GameKey { get; set; }

    public required string ModId { get; set; }

    public required string ModName { get; set; }

    public string? FileId { get; set; }

    public string? FileName { get; set; }

    public string? VersionLabel { get; set; }

    public string? RelativePath { get; set; }

    public string? ReplacesFileId { get; set; }

    public string? ReplacesVersion { get; set; }

    public bool ByRule { get; set; }

    public static LibraryEventRecord From(LibraryEvent happened) => new()
    {
        At = happened.At,
        Kind = happened.Kind,
        SourceId = happened.Mod.Game.Source.Value,
        GameKey = happened.Mod.Game.Key,
        ModId = happened.Mod.Id,
        ModName = happened.ModName,
        FileId = happened.File?.Id,
        FileName = happened.FileName,
        VersionLabel = happened.VersionLabel,
        RelativePath = happened.RelativePath,
        ReplacesFileId = happened.Replaces?.Id,
        ReplacesVersion = happened.ReplacesVersion,
        ByRule = happened.ByRule,
    };

    public LibraryEvent ToEvent()
    {
        ModRef mod = new(new GameRef(new SourceId(SourceId), GameKey), ModId);
        return new LibraryEvent(
            At,
            Kind,
            mod,
            ModName,
            FileId is null ? null : new ModFileRef(mod, FileId),
            FileName,
            VersionLabel,
            RelativePath,
            ReplacesFileId is null ? null : new ModFileRef(mod, ReplacesFileId),
            ReplacesVersion,
            ByRule);
    }
}
