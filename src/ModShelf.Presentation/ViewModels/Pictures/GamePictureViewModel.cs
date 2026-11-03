using CommunityToolkit.Mvvm.ComponentModel;

namespace ModShelf.Presentation.ViewModels.Pictures;

/// <summary>
/// A game's picture on its cover, such as its banner on the site: the saved file, once there is one. Until then, and when
/// the site gives none, can't be reached, or the screen shows no pictures, the cover shows the game's initial on its
/// colour. A newer picture asked for replaces one still being read.
/// </summary>
public sealed partial class GamePictureViewModel : ObservableObject
{
    private readonly ModPictures _pictures;
    private Uri? _address;
    private Uri? _reading;
    private bool _isShown;
    private int _asked;

    internal GamePictureViewModel(ModPictures pictures)
    {
        ArgumentNullException.ThrowIfNull(pictures);

        _pictures = pictures;
    }

    /// <summary>
    /// The picture's file; null while there's none to show: being read, none to read, or not shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    public partial string? File { get; private set; }

    public bool HasFile => File is not null;

    /// <summary>
    /// The picture being read, done once it's shown; it never throws.
    /// </summary>
    internal Task Showing { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Says where the game's site keeps the picture, null for nowhere; while the cover shows pictures, it shows this one.
    /// </summary>
    internal void Place(Uri? address, CancellationToken cancellationToken)
    {
        _address = address;
        if (_isShown)
        {
            Read(cancellationToken);
        }
    }

    /// <summary>
    /// Shows the picture from now on, reading it unless it's read already.
    /// </summary>
    internal void Show(CancellationToken cancellationToken)
    {
        _isShown = true;
        Read(cancellationToken);
    }

    /// <summary>
    /// Shows no picture from now on, and forgets one still being read.
    /// </summary>
    internal void Hide()
    {
        _isShown = false;
        _asked++;
        _reading = null;
        File = null;
        Showing = Task.CompletedTask;
    }

    private void Read(CancellationToken cancellationToken)
    {
        if (_address == _reading)
        {
            return;
        }

        int asked = ++_asked;
        _reading = _address;
        File = null;
        Showing = _address is { } address ? ReadAsync(address, asked, cancellationToken) : Task.CompletedTask;
    }

    private async Task ReadAsync(Uri address, int asked, CancellationToken cancellationToken)
    {
        try
        {
            string? file = await _pictures.Cache.GetAsync(address, cancellationToken);
            if (asked == _asked)
            {
                File = file;
            }
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
}
