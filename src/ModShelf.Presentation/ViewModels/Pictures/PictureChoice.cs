using ModShelf.Application.Settings;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Pictures;

/// <summary>
/// Which of a mod's pictures to show, and how.
/// </summary>
/// <param name="Address">The picture to show; null for none.</param>
/// <param name="IsBlurred">Whether the view blurs it: an adult mod's picture its site gives no blurred version of.</param>
/// <param name="IsHidden">Whether it's an adult mod's picture, hidden: the view shows a sign instead.</param>
/// <param name="IsAdultBlurred">Whether it's an adult mod's picture, blurred by its site or the view.</param>
internal readonly record struct PictureChoice(Uri? Address, bool IsBlurred, bool IsHidden, bool IsAdultBlurred)
{
    /// <summary>
    /// The mod's thumbnail, or its picture when its site gives none. An adult mod's is hidden, its site's blurred
    /// thumbnail, the picture blurred by the view, or shown, as <paramref name="adult"/> says.
    /// </summary>
    public static PictureChoice Of(ModInfo? details, AdultPictures adult)
    {
        Uri? picture = details?.ThumbnailUrl ?? details?.PictureUrl;
        if (details is not { HasAdultContent: true } || adult == AdultPictures.Shown)
        {
            return new PictureChoice(picture, IsBlurred: false, IsHidden: false, IsAdultBlurred: false);
        }

        if (adult == AdultPictures.Hidden)
        {
            return new PictureChoice(null, IsBlurred: false, IsHidden: true, IsAdultBlurred: false);
        }

        return details.ThumbnailBlurredUrl is { } blurred
            ? new PictureChoice(blurred, IsBlurred: false, IsHidden: false, IsAdultBlurred: true)
            : new PictureChoice(picture, IsBlurred: picture is not null, IsHidden: false, IsAdultBlurred: true);
    }
}
