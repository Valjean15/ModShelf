using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Endorsements;

namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// Endorsing the selected mods at once, on a site that lets ModShelf endorse (ADR 0100): it says which mods first, asks
/// the site once a mod, and says what the site refused, in its words. Which mods the account endorsed is read once a
/// site, when one of its games opens.
/// </summary>
public sealed partial class LibraryViewModel
{
    /// <summary>
    /// The most mods named when asking before endorsing; the others are counted.
    /// </summary>
    private const int MaxNamesAsked = 10;

    /// <summary>
    /// Such as "Endorse (2)": how many of the selected mods the account hasn't endorsed; empty on a site that can't.
    /// </summary>
    [ObservableProperty]
    public partial string EndorseSelectedText { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the open game's site lets ModShelf endorse, so Endorse shows beside the selection's other buttons.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowsEndorseSelected { get; private set; }

    /// <summary>
    /// Whether some of the selected mods aren't endorsed, while nothing else is done to the selection.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EndorseSelectedCommand))]
    public partial bool CanEndorseSelected { get; private set; }

    /// <summary>
    /// Endorses the selected mods the account hasn't, after saying which, one request a mod. What the site refuses, such
    /// as a mod the account didn't download, is said once, mod by mod, in the site's words; those mods stay selected.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEndorseSelected))]
    private async Task EndorseSelectedAsync(CancellationToken cancellationToken)
    {
        LibraryModViewModel[] mods = [.. GameMods.Where(mod => mod.IsSelected && mod.IsEndorsed != true)];
        if (mods.Length == 0)
        {
            return;
        }

        SourceId source = mods[0].Mod.Game.Source;
        string site = _sources.SiteName(source);
        SiteWords words = _sources.Words(source);
        string approve = Text.Capitalized(words.Approve ?? string.Empty);
        string names = mods.Length <= MaxNamesAsked
            ? string.Join(Environment.NewLine, mods.Select(mod => mod.Name))
            : string.Join(Environment.NewLine, [.. mods.Take(MaxNamesAsked).Select(mod => mod.Name), Text.Format(Strings.LibraryEndorseMore, mods.Length - MaxNamesAsked)]);
        string title = Counts.Of(mods.Length, Text.Format(Strings.LibraryEndorseAskOne, approve, site), Text.Format(Strings.LibraryEndorseAsk, approve, mods.Length, site));
        if (!await _services.Dialogs.ConfirmAsync(title, names, approve, cancellationToken))
        {
            return;
        }

        IsChangingSelected = true;
        ShowSelection();
        List<LibraryModViewModel> done = [];
        List<string> problems = [];
        try
        {
            foreach (LibraryModViewModel mod in mods)
            {
                try
                {
                    await _pageServices.Endorsed.SetAsync(mod.Mod, Endorsement.Endorsed, null, cancellationToken);
                    mod.ShowEndorsed(true);
                    done.Add(mod);
                }
                catch (Exception exception) when (SiteProblems.Describe(exception, site, Strings.LibraryEndorseSelectedProblem) is { } problem)
                {
                    problems.Add(Text.Format(Strings.LibrarySelectedProblem, mod.Name, problem));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogSelectedChangeFailed(_logger, exception, mod.Mod.Id, mod.Mod.Game.Key);
                    problems.Add(Text.Format(Strings.LibrarySelectedProblem, mod.Name, Strings.SomethingWentWrong));
                }
            }
        }
        finally
        {
            IsChangingSelected = false;
            Unselect(done);
            ArrangeMods();
        }

        if (problems.Count > 0)
        {
            await _services.Dialogs.ShowMessageAsync(
                Text.Format(Strings.LibraryEndorseSelectedFailed, approve.ToLowerInvariant()),
                string.Join(Environment.NewLine, problems),
                cancellationToken);
        }
    }

    /// <summary>
    /// Says what the endorse button would do to the selected mods, on a site that endorses.
    /// </summary>
    private void ShowSelectedEndorsements(LibraryModViewModel[] selected)
    {
        SourceId? source = OpenGame?.Game.Source;
        ShowsEndorseSelected = source is not null && _pageServices.Endorsed.CanEndorse(source) && _sources.Words(source).CanApprove;
        int toEndorse = selected.Count(mod => mod.IsEndorsed != true);
        EndorseSelectedText = ShowsEndorseSelected
            ? Text.Format(Strings.LibraryEndorseSelected, Text.Capitalized(_sources.Words(source!).Approve!), toEndorse)
            : string.Empty;
        CanEndorseSelected = ShowsEndorseSelected && !IsChangingSelected && toEndorse > 0;
    }

    /// <summary>
    /// Reads which mods the account endorsed on each of the sites that let ModShelf endorse, once a site. When it can't
    /// be read, such as without a linked account, it's logged, and the mods' endorsements stay unknown.
    /// </summary>
    private async Task ReadEndorsementsAsync(IReadOnlyCollection<SourceId> sources, CancellationToken cancellationToken)
    {
        foreach (SourceId source in sources.Where(source => _pageServices.Endorsed.CanEndorse(source) && !_pageServices.Endorsed.Knows(source)))
        {
            try
            {
                await _pageServices.Endorsed.ReadAsync(source, cancellationToken);
            }
            catch (Exception exception) when (SiteProblems.IsSiteProblem(exception))
            {
                // Expected without a linked account, or offline: no stack trace for it.
                LogEndorsementsUnknown(_logger, source.Value, exception.Message);
            }
        }
    }
}
