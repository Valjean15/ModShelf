using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.ModPages;

/// <summary>
/// Something the mod needs, as its author listed it: another mod, whose page ModShelf opens so its files can be
/// downloaded, or a thing off the site, whose page opens in the browser. Nothing is installed.
/// </summary>
public sealed partial class RequirementViewModel : ObservableObject
{
    private readonly ModRequirement _requirement;
    private readonly ModPageServices _services;
    private readonly Func<ModRef, CancellationToken, Task> _open;

    /// <param name="open">Opens a mod's page on the screen.</param>
    internal RequirementViewModel(ModRequirement requirement, ModPageServices services, Func<ModRef, CancellationToken, Task> open)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(open);

        _requirement = requirement;
        _services = services;
        _open = open;
        Name = requirement.Name;
        Notes = requirement.Notes ?? string.Empty;
        IsMod = requirement.Mod is not null;
        IsElsewhere = requirement.Mod is null && requirement.PageUrl is not null;
    }

    public string Name { get; }

    /// <summary>
    /// What the author said of it, such as which files need it; empty for nothing.
    /// </summary>
    public string Notes { get; }

    /// <summary>
    /// Whether it's a mod of the site, whose page ModShelf can open.
    /// </summary>
    public bool IsMod { get; }

    /// <summary>
    /// Whether it's a thing off the site with a page of its own, which opens in the browser.
    /// </summary>
    public bool IsElsewhere { get; }

    /// <summary>
    /// Whether the library has files of the mod needed.
    /// </summary>
    [ObservableProperty]
    public partial bool IsInLibrary { get; private set; }

    internal ModRef? Mod => _requirement.Mod;

    /// <summary>
    /// Shows whether the library has files of the mod needed. Runs on the UI thread.
    /// </summary>
    internal void ShowLibrary(bool isInLibrary) => IsInLibrary = isInLibrary;

    /// <summary>
    /// Opens the mod needed on the screen, where its files can be downloaded.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsMod))]
    private Task OpenAsync(CancellationToken cancellationToken) => _requirement.Mod is { } mod ? _open(mod, cancellationToken) : Task.CompletedTask;

    /// <summary>
    /// Opens the thing's own page in the browser. When no browser opens, the page's address is shown instead.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsElsewhere))]
    private Task OpenPageAsync(CancellationToken cancellationToken)
        => _requirement.PageUrl is { } page
            ? Pages.OpenAsync(page, _services.Browser, _services.Dialogs, Strings.OpenPageByHand, cancellationToken)
            : Task.CompletedTask;
}
