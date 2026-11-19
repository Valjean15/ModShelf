using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Requirements;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.ModPages;

/// <summary>
/// One requirement in the dialog that downloads a mod with them: one to download, ticked unless the user unticks it;
/// one the library has or is downloading; a thing off the site, whose page opens in the browser; or one that can't be
/// downloaded, and why.
/// </summary>
public sealed partial class RequirementRowViewModel : ObservableObject
{
    private readonly ModPageServices _services;

    internal RequirementRowViewModel(PlannedRequirement requirement, string? neededBy, bool chosen, ModPageServices services)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(services);

        Requirement = requirement;
        _services = services;
        Name = requirement.Name;
        Notes = requirement.Notes ?? string.Empty;
        NeededByText = requirement.Depth > 1 && neededBy is not null ? Text.Format(Strings.RequirementNeededBy, neededBy) : string.Empty;
        CanChoose = requirement.State == RequirementState.ToDownload;
        IsChosen = CanChoose && chosen;
        IsElsewhere = requirement is { State: RequirementState.Elsewhere, PageUrl: not null };
        Said = requirement.State switch
        {
            RequirementState.ToDownload => Text.Format(Strings.RequirementFile, requirement.File!.Name),
            RequirementState.InLibrary => Strings.ModPageInLibrary,
            RequirementState.Downloading => Strings.RequirementDownloading,
            RequirementState.Elsewhere => Strings.RequirementElsewhere,
            _ => Text.Format(Strings.RequirementCantDownload, requirement.Why ?? Strings.SomethingWentWrong),
        };
    }

    /// <summary>
    /// Raised when the user ticks or unticks it.
    /// </summary>
    public event EventHandler? ChosenChanged;

    internal PlannedRequirement Requirement { get; }

    public string Name { get; }

    /// <summary>
    /// What the author said of it, such as which files need it; empty for nothing.
    /// </summary>
    public string Notes { get; }

    /// <summary>
    /// For a requirement of a requirement, such as "Needed by SkyUI"; empty for what the mod itself needs.
    /// </summary>
    public string NeededByText { get; }

    /// <summary>
    /// What downloading does with it, such as "Newest main file: SkyUI 5.2" or "In the library".
    /// </summary>
    public string Said { get; }

    /// <summary>
    /// Whether it's to be downloaded, so it can be ticked.
    /// </summary>
    public bool CanChoose { get; }

    /// <summary>
    /// Whether it's a thing off the site with a page of its own.
    /// </summary>
    public bool IsElsewhere { get; }

    /// <summary>
    /// Whether it's ticked, to be downloaded.
    /// </summary>
    [ObservableProperty]
    public partial bool IsChosen { get; set; }

    partial void OnIsChosenChanged(bool value) => ChosenChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Opens the thing's own page in the browser. When no browser opens, the page's address is shown instead.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsElsewhere))]
    private Task OpenPageAsync(CancellationToken cancellationToken)
        => Requirement.PageUrl is { } page
            ? Pages.OpenAsync(page, _services.Browser, _services.Dialogs, Strings.OpenPageByHand, cancellationToken)
            : Task.CompletedTask;
}
