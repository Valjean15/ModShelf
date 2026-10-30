using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Links;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// A site's links, such as Nexus Mods' <c>nxm://</c> links, on the Settings screen: which app opens them, and making
/// ModShelf open them, or giving them back.
/// </summary>
public sealed partial class LinkHandlerViewModel : ObservableObject
{
    private readonly ILinkHandlers _handlers;
    private readonly Action<Exception> _logTakeFailed;
    private readonly Action<Exception> _logGiveBackFailed;

    internal LinkHandlerViewModel(LinkHandler handler, ILinkHandlers handlers, Action<Exception> logTakeFailed, Action<Exception> logGiveBackFailed)
    {
        _handlers = handlers;
        _logTakeFailed = logTakeFailed;
        _logGiveBackFailed = logGiveBackFailed;
        Show(handler);
    }

    /// <summary>
    /// The site and its links, as the system opens them now.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(CanTake), nameof(CanGiveBack), nameof(GiveBackText), nameof(OffersTake), nameof(OffersGiveBack))]
    [NotifyCanExecuteChangedFor(nameof(TakeCommand), nameof(GiveBackCommand))]
    public partial LinkHandler Handler { get; private set; }

    /// <summary>
    /// Such as "Vortex opens nxm:// links from Nexus Mods."
    /// </summary>
    public string StateText => Handler.State switch
    {
        { Kind: SchemeHandlerKind.ModShelf } => Text.Format(Strings.LinksOpenedByModShelf, Handler.Scheme, Handler.SiteName),
        { Kind: SchemeHandlerKind.OtherApp, OtherApp: { } app } => Text.Format(Strings.LinksOpenedByApp, Handler.Scheme, Handler.SiteName, app),
        { Kind: SchemeHandlerKind.OtherApp } => Text.Format(Strings.LinksOpenedByAnotherApp, Handler.Scheme, Handler.SiteName),
        { Kind: SchemeHandlerKind.Unavailable } => Text.Format(Strings.LinksUnavailable, Handler.Scheme, Handler.SiteName),
        _ => Text.Format(Strings.LinksOpenedByNone, Handler.Scheme, Handler.SiteName),
    };

    /// <summary>
    /// Whether to offer taking the links: another app opens them, or none does.
    /// </summary>
    public bool OffersTake => Handler.State.Kind is SchemeHandlerKind.None or SchemeHandlerKind.OtherApp;

    /// <summary>
    /// Whether to offer giving the links back: ModShelf opens them.
    /// </summary>
    public bool OffersGiveBack => Handler.State.Kind == SchemeHandlerKind.ModShelf;

    /// <summary>
    /// Whether ModShelf can take the links now: another app opens them, or none does, and no command is running.
    /// </summary>
    public bool CanTake => !IsBusy && OffersTake;

    /// <summary>
    /// Whether ModShelf can give the links back now: it opens them, and no command is running.
    /// </summary>
    public bool CanGiveBack => !IsBusy && OffersGiveBack;

    /// <summary>
    /// Such as "Give them back to Vortex", or "Stop opening them" when no app had them before.
    /// </summary>
    public string GiveBackText
        => Handler.State.PreviousApp is { } previous ? Text.Format(Strings.LinksGiveBackTo, previous) : Strings.LinksGiveBack;

    /// <summary>
    /// What the last command did, or why it didn't; empty for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    /// <summary>
    /// Whether <see cref="Note"/> says something went wrong.
    /// </summary>
    [ObservableProperty]
    public partial bool IsNoteAProblem { get; private set; }

    /// <summary>
    /// Whether a command is changing the system's apps for links.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTake), nameof(CanGiveBack))]
    [NotifyCanExecuteChangedFor(nameof(TakeCommand), nameof(GiveBackCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>
    /// Shows the site's links as the system opens them now, such as after the Settings screen read them again.
    /// </summary>
    internal void Show(LinkHandler handler) => Handler = handler;

    /// <summary>
    /// Makes the system open the site's links with ModShelf, and says when it still prefers an app chosen in its own
    /// settings.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTake))]
    private Task TakeAsync(CancellationToken cancellationToken)
        => ChangeAsync(
            async () =>
            {
                Show(await _handlers.TakeAsync(Handler.Source, cancellationToken));
                Tell(
                    Handler.State is { Kind: SchemeHandlerKind.OtherApp } chosen
                        ? Text.Format(Strings.LinksStillChosen, chosen.OtherApp ?? Handler.SiteName, Handler.Scheme)
                        : string.Empty,
                    isProblem: false);
            },
            _logTakeFailed,
            cancellationToken);

    /// <summary>
    /// Stops the system opening the site's links with ModShelf, giving them back to the app that had them before.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGiveBack))]
    private Task GiveBackAsync(CancellationToken cancellationToken)
        => ChangeAsync(
            async () =>
            {
                Show(await _handlers.GiveBackAsync(Handler.Source, cancellationToken));
                Tell(
                    Handler.State.Kind == SchemeHandlerKind.ModShelf ? Text.Format(Strings.LinksStillModShelf, Handler.Scheme) : string.Empty,
                    isProblem: false);
            },
            _logGiveBackFailed,
            cancellationToken);

    private async Task ChangeAsync(Func<Task> change, Action<Exception> log, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await UserCommands.RunInPlaceAsync(
                change,
                exception => exception switch
                {
                    InvalidOperationException => Strings.LinksCantRegisterHere,
                    IOException or UnauthorizedAccessException => Text.Format(Strings.LinksCantChange, exception.Message),
                    _ => null,
                },
                problem => Tell(problem, isProblem: true),
                log,
                cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Tell(string note, bool isProblem)
    {
        Note = note;
        IsNoteAProblem = isProblem;
    }
}
