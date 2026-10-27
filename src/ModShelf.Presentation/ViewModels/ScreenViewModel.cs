using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// One of the app's screens, as the sidebar lists it and the main window shows it.
/// </summary>
public abstract partial class ScreenViewModel : ObservableObject
{
    /// <summary>
    /// Which screen this is.
    /// </summary>
    public abstract Screen Screen { get; }

    /// <summary>
    /// Its name, in the sidebar and as its heading.
    /// </summary>
    public abstract string Title { get; }

    /// <summary>
    /// How many things on the screen want the user, such as downloads waiting for them; 0 for none, which shows no
    /// badge.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    public partial int Badge { get; protected set; }

    /// <summary>
    /// Whether the sidebar shows <see cref="Badge"/>.
    /// </summary>
    public bool HasBadge => Badge > 0;

    /// <summary>
    /// The screen's work for the app's lifetime, whether it's shown or not, such as watching the download queue so its
    /// badge stays right. The main window starts it once, on the UI thread, and cancels
    /// <paramref name="cancellationToken"/> when the app closes. A screen without such work has nothing to do.
    /// </summary>
    public virtual Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
