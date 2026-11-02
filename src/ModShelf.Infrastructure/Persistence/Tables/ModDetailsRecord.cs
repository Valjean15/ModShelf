using ModShelf.Application.Catalog.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// A row of the <c>ModDetails</c> table: a mod's details as its site last gave them, and when. It maps to and from
/// <see cref="SavedModDetails"/>.
/// </summary>
/// <remarks>A mutable class rather than a record, because EF Core tracks entities by reference.</remarks>
internal sealed class ModDetailsRecord
{
    public required string SourceId { get; set; }

    public required string GameKey { get; set; }

    public required string ModId { get; set; }

    public required string Name { get; set; }

    public string? Summary { get; set; }

    public string? Author { get; set; }

    public string? Version { get; set; }

    public string? PageUrl { get; set; }

    public string? PictureUrl { get; set; }

    public string? ThumbnailUrl { get; set; }

    public string? ThumbnailBlurredUrl { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public bool IsAvailable { get; set; }

    public bool HasAdultContent { get; set; }

    public long? Downloads { get; set; }

    public long? Endorsements { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    public static ModDetailsRecord From(SavedModDetails saved)
    {
        ModRef mod = saved.Details.Mod;
        ModDetailsRecord record = new()
        {
            SourceId = mod.Game.Source.Value,
            GameKey = mod.Game.Key,
            ModId = mod.Id,
            Name = saved.Details.Name,
        };
        record.CopyFrom(saved);
        return record;
    }

    /// <summary>
    /// Takes over everything but the key from <paramref name="saved"/>.
    /// </summary>
    public void CopyFrom(SavedModDetails saved)
    {
        ModInfo details = saved.Details;
        Name = details.Name;
        Summary = details.Summary;
        Author = details.Author;
        Version = details.Version;
        PageUrl = details.PageUrl?.AbsoluteUri;
        PictureUrl = details.PictureUrl?.AbsoluteUri;
        ThumbnailUrl = details.ThumbnailUrl?.AbsoluteUri;
        ThumbnailBlurredUrl = details.ThumbnailBlurredUrl?.AbsoluteUri;
        UpdatedAt = details.UpdatedAt;
        IsAvailable = details.IsAvailable;
        HasAdultContent = details.HasAdultContent;
        Downloads = details.Downloads;
        Endorsements = details.Endorsements;
        SavedAt = saved.SavedAt;
    }

    /// <exception cref="InvalidDataException">The row holds values no details can have.</exception>
    public SavedModDetails ToSaved()
    {
        try
        {
            ModInfo details = new(
                new ModRef(new GameRef(new SourceId(SourceId), GameKey), ModId),
                Name,
                Summary,
                Author,
                Version,
                Link(PageUrl),
                Link(PictureUrl),
                UpdatedAt,
                IsAvailable,
                HasAdultContent,
                Downloads,
                Endorsements,
                Link(ThumbnailUrl),
                Link(ThumbnailBlurredUrl));
            return new SavedModDetails(details, SavedAt);
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            throw new InvalidDataException(
                $"The details of mod {ModId} of {GameKey} on {SourceId} in the library database can't be loaded: {exception.Message}", exception);
        }
    }

    private static Uri? Link(string? link) => link is null ? null : new Uri(link, UriKind.Absolute);
}
