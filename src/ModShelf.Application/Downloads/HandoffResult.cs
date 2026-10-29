namespace ModShelf.Application.Downloads;

/// <summary>
/// What <see cref="ILinkDownloads.HandOverAsync"/> made of a link.
/// </summary>
/// <param name="OtherFileName">
/// For <see cref="HandoffAnswer.OtherFile"/>, the name the link gives the other file, when it gives one.
/// </param>
public sealed record HandoffResult(HandoffAnswer Answer, string? OtherFileName = null);
