using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Pictures;

/// <summary>
/// A mod's picture, as a row's thumbnail or a card's picture show it: the saved file, once there is one, and whether
/// it's an adult mod's, blurred or hidden. A newer picture asked for replaces one still being read.
/// </summary>
public sealed partial class ModPictureViewModel : ObservableObject
{
    private readonly ModPictures _pictures;
    private ModInfo? _details;
    private Uri? _address;
    private int _asked;

    internal ModPictureViewModel(ModPictures pictures)
    {
        ArgumentNullException.ThrowIfNull(pictures);

        _pictures = pictures;
    }

    /// <summary>
    /// Whether the screen shows the picture, as a thumbnail or card; false once hidden.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    /// <summary>
    /// The picture's file; null while there's none to show: being read, none to read, hidden, or not shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile), nameof(ShowsPlaceholder))]
    public partial string? File { get; private set; }

    public bool HasFile => File is not null;

    /// <summary>
    /// Whether the view shows a sign for a picture rather than one: none yet, or none at all; not for one hidden.
    /// </summary>
    public bool ShowsPlaceholder => File is null && !IsHidden;

    /// <summary>
    /// Whether the view blurs the picture: an adult mod's, whose site gives no blurred version of it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBlurred { get; private set; }

    /// <summary>
    /// Whether it's an adult mod's picture, hidden as the settings say: the view shows a sign instead.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPlaceholder))]
    public partial bool IsHidden { get; private set; }

    /// <summary>
    /// Whether it's an adult mod's picture, blurred, which cards say on it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAdultBlurred { get; private set; }

    /// <summary>
    /// What was asked for last, done once this is; it never throws.
    /// </summary>
    internal Task Showing { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts <see cref="ShowAsync(ModRef, CancellationToken)"/>, logging what goes wrong.
    /// </summary>
    internal void Show(ModRef mod, CancellationToken cancellationToken) => Showing = RunAsync(ShowAsync(mod, cancellationToken), cancellationToken);

    /// <summary>
    /// Starts <see cref="ShowAsync(ModInfo, CancellationToken)"/>, logging what goes wrong.
    /// </summary>
    internal void Show(ModInfo details, CancellationToken cancellationToken) => Showing = RunAsync(ShowAsync(details, cancellationToken), cancellationToken);

    /// <summary>
    /// Starts <see cref="RefreshAsync"/>, logging what goes wrong.
    /// </summary>
    internal void Refresh(CancellationToken cancellationToken) => Showing = RunAsync(RefreshAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Shows the picture the mod's saved details name at once, then the one its site names when they're old.
    /// </summary>
    internal async Task ShowAsync(ModRef mod, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mod);

        int asked = ++_asked;
        IsShown = true;
        ModInfo? saved = await _pictures.Details.GetSavedAsync(mod, cancellationToken);
        if (asked != _asked)
        {
            return;
        }

        Task showingSaved = saved is null ? Task.CompletedTask : ShowAsync(saved, asked, cancellationToken);
        ModInfo? details = await _pictures.Details.GetAsync(mod, cancellationToken);
        await showingSaved;
        if (asked == _asked && details is not null && details != saved)
        {
            await ShowAsync(details, asked, cancellationToken);
        }
    }

    /// <summary>
    /// Shows the picture the details name.
    /// </summary>
    internal Task ShowAsync(ModInfo details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);

        return ShowAsync(details, ++_asked, cancellationToken);
    }

    /// <summary>
    /// Shows the picture of the details shown last again, such as after adult pictures changed from blurred to shown.
    /// </summary>
    internal Task RefreshAsync(CancellationToken cancellationToken)
        => _details is null ? Task.CompletedTask : ShowAsync(_details, ++_asked, cancellationToken);

    /// <summary>
    /// Shows nothing, and forgets a picture still being read.
    /// </summary>
    internal void Hide()
    {
        _asked++;
        IsShown = false;
        _details = null;
        _address = null;
        File = null;
        IsBlurred = false;
        IsHidden = false;
        IsAdultBlurred = false;
    }

    private async Task RunAsync(Task showing, CancellationToken cancellationToken)
    {
        try
        {
            await showing;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (Exception exception)
        {
            LogShowFailed(_pictures.Logger, exception);
        }
    }

    private async Task ShowAsync(ModInfo details, int asked, CancellationToken cancellationToken)
    {
        _details = details;
        IsShown = true;
        PictureChoice choice = PictureChoice.Of(details, _pictures.Adult);
        IsBlurred = choice.IsBlurred;
        IsHidden = choice.IsHidden;
        IsAdultBlurred = choice.IsAdultBlurred;
        if (choice.Address == _address)
        {
            return;
        }

        _address = choice.Address;
        File = null;
        if (choice.Address is null)
        {
            return;
        }

        string? file = await _pictures.Cache.GetAsync(choice.Address, cancellationToken);
        if (asked == _asked)
        {
            File = file;
        }
    }
}
