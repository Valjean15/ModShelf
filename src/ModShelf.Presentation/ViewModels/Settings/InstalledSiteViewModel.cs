using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Installed;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// An installed game's game on a site, in the dialog choosing its games: what it is, with <b>None here</b> and
/// <b>Match by name</b>.
/// </summary>
public sealed partial class InstalledSiteViewModel
{
    private readonly Action<SourceId> _none;
    private readonly Action<SourceId> _byName;

    internal InstalledSiteViewModel(SiteMatch match, string siteName, Action<SourceId> none, Action<SourceId> byName)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteName);
        ArgumentNullException.ThrowIfNull(none);
        ArgumentNullException.ThrowIfNull(byName);

        Site = match.Site;
        SiteName = siteName;
        _none = none;
        _byName = byName;
        MatchText = (match.Picked, match.Games.Count) switch
        {
            (true, 0) => Strings.InstalledSiteNoneChosen,
            (true, _) => Text.Format(Strings.InstalledSiteChosen, match.Games[0].Name),
            (false, 1) => Text.Format(Strings.InstalledSiteByName, match.Games[0].Name),
            _ => Text.Format(Strings.InstalledSiteShared, match.Games.Count),
        };
        CanSayNone = !match.Picked || match.Games.Count > 0;
        CanMatchByName = match.Picked;
    }

    public SourceId Site { get; }

    /// <summary>
    /// Such as "Nexus Mods".
    /// </summary>
    public string SiteName { get; }

    /// <summary>
    /// Such as "Skyrim Special Edition, chosen", "None, chosen", or "2 games of this name: choose one below".
    /// </summary>
    public string MatchText { get; }

    /// <summary>
    /// Whether <b>None here</b> shows: unless none is what was chosen.
    /// </summary>
    public bool CanSayNone { get; }

    /// <summary>
    /// Whether <b>Match by name</b> shows: when the game, or none, was chosen.
    /// </summary>
    public bool CanMatchByName { get; }

    [RelayCommand]
    private void None() => _none(Site);

    [RelayCommand]
    private void ByName() => _byName(Site);
}
