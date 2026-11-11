namespace ModShelf.Presentation.ViewModels.Accounts;

/// <summary>
/// A site ModShelf uses without an account, such as DEG Mods or web links, on Settings › Accounts: its name, and what
/// ModShelf does there.
/// </summary>
public sealed record SiteWithoutAccountViewModel
{
    internal SiteWithoutAccountViewModel(string siteName, string aboutText)
    {
        SiteName = siteName;
        AboutText = aboutText;
    }

    /// <summary>
    /// Such as "DEG Mods".
    /// </summary>
    public string SiteName { get; }

    /// <summary>
    /// Such as "ModShelf searches its mods, downloads its files and checks them for updates, with no account."
    /// </summary>
    public string AboutText { get; }
}
