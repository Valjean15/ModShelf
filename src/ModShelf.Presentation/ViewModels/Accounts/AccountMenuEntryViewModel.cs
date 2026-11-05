using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.Accounts;

/// <summary>
/// An account at the bottom of the menu: its picture, or its initial on its colour while there's none, as when the site
/// gives none or can't be reached before the picture is saved; its name; and its level and site, such as "Premium · Nexus
/// Mods". The menu, collapsed, shows the picture alone.
/// </summary>
public sealed partial class AccountMenuEntryViewModel : ObservableObject
{
    internal AccountMenuEntryViewModel(SourceId source, ModPictures pictures)
    {
        ArgumentNullException.ThrowIfNull(source);

        Source = source;
        Picture = new GamePictureViewModel(pictures);
        Picture.Show(CancellationToken.None);
    }

    public SourceId Source { get; }

    /// <summary>
    /// The account's picture on its site.
    /// </summary>
    public GamePictureViewModel Picture { get; }

    /// <summary>
    /// The account's name on its site.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initial), nameof(Hue), nameof(Tip))]
    public partial string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "Premium · Nexus Mods", or the site's name alone when it has no levels.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tip))]
    public partial string Level { get; private set; } = string.Empty;

    /// <summary>
    /// The first letter of the account's name, in place of its picture while there's none.
    /// </summary>
    public string Initial => Covers.InitialOf(Name);

    /// <summary>
    /// The hue behind the initial, from the account's name.
    /// </summary>
    public int Hue => Covers.HueOf(Name);

    /// <summary>
    /// Such as "Kestrel · Premium · Nexus Mods", which the collapsed menu shows on the picture.
    /// </summary>
    public string Tip => Text.Format(Strings.AccountMenuTip, Name, Level);

    internal void Show(LinkedAccount account, string siteName)
    {
        ArgumentNullException.ThrowIfNull(account);

        Name = account.Profile.DisplayName;
        Level = account.Profile.Tier is { } tier ? Text.Format(Strings.AccountMenuLevel, tier, siteName) : siteName;
        Picture.Place(account.Profile.AvatarUrl, CancellationToken.None);
    }
}
