namespace ModShelf.Presentation.Services;

/// <summary>
/// The thread the views run on. ViewModels change what views show only there, so what arrives from elsewhere, such as
/// a background check, goes through <see cref="Post"/>.
/// </summary>
public interface IUiThread
{
    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, soon; at once when the caller is on it, or when there's no UI,
    /// as in tests.
    /// </summary>
    void Post(Action action);
}
