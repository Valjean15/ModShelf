namespace ModShelf.Presentation.Services;

/// <summary>
/// Navigation as plain state: the views follow <see cref="Current"/>, so nothing here needs the UI.
/// </summary>
internal sealed class NavigationService : INavigationService
{
    private readonly Lock _gate = new();
    private Screen _current = Screen.Library;

    public event EventHandler<Screen>? Navigated;

    public Screen Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void NavigateTo(Screen screen)
    {
        if (!Enum.IsDefined(screen))
        {
            throw new ArgumentOutOfRangeException(nameof(screen), screen, "There's no such screen.");
        }

        lock (_gate)
        {
            if (_current == screen)
            {
                return;
            }

            _current = screen;
        }

        Navigated?.Invoke(this, screen);
    }
}
