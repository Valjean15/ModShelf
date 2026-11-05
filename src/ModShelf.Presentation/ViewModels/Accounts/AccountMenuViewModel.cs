using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.Accounts;

/// <summary>
/// The accounts at the bottom of the menu: each one linked, with its picture, its name, and its level and site, in the
/// order of the sites; or, with none linked, Link an account. Each opens the Accounts screen, which tells it of every
/// site's account as it lists them, links one, checks it or unlinks it.
/// </summary>
public sealed partial class AccountMenuViewModel : ObservableObject
{
    private readonly ModPictures _pictures;
    private readonly INavigationService _navigation;
    private readonly List<SourceId> _sites = [];
    private readonly Dictionary<SourceId, AccountMenuEntryViewModel> _linked = [];

    public AccountMenuViewModel(ModPictures pictures, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(navigation);

        _pictures = pictures;
        _navigation = navigation;
    }

    /// <summary>
    /// The accounts linked, in the order of their sites.
    /// </summary>
    public ObservableCollection<AccountMenuEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    public partial bool HasAccounts { get; private set; }

    /// <summary>
    /// Whether to offer Link an account: some site can link one, and none is linked. Not before the sites are known.
    /// </summary>
    [ObservableProperty]
    public partial bool OffersLinking { get; private set; }

    /// <summary>
    /// Shows the account linked on a site, or that none is. The sites keep the order they're first shown in.
    /// </summary>
    /// <param name="siteName">The site's name, such as Nexus Mods.</param>
    /// <param name="account">The account; null for none.</param>
    internal void Show(SourceId source, string siteName, LinkedAccount? account)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteName);

        if (!_sites.Contains(source))
        {
            _sites.Add(source);
        }

        if (account is null)
        {
            _linked.Remove(source);
        }
        else
        {
            if (!_linked.TryGetValue(source, out AccountMenuEntryViewModel? entry))
            {
                entry = new AccountMenuEntryViewModel(source, _pictures);
                _linked.Add(source, entry);
            }

            entry.Show(account, siteName);
        }

        Entries.SyncWith([.. _sites.Where(_linked.ContainsKey).Select(site => _linked[site])]);
        HasAccounts = Entries.Count > 0;
        OffersLinking = !HasAccounts;
    }

    [RelayCommand]
    private void OpenAccounts() => _navigation.NavigateTo(Screen.Accounts);
}
