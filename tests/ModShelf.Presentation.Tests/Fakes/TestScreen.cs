using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A screen whose work the test gives, and which counts how often it was started and shown.
/// </summary>
/// <param name="work">The screen's work; by default, waiting until the app closes.</param>
internal sealed class TestScreen(Screen screen, Func<CancellationToken, Task>? work = null) : ScreenViewModel
{
    private int _starts;
    private int _shows;

    public override Screen Screen => screen;

    public override string Title => screen.ToString();

    public int Starts => Volatile.Read(ref _starts);

    public int Shows => Volatile.Read(ref _shows);

    public static IEnumerable<TestScreen> OneOfEach() => Enum.GetValues<Screen>().Select(screen => new TestScreen(screen));

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _starts);
        return work is null ? Task.Delay(Timeout.Infinite, cancellationToken) : work(cancellationToken);
    }

    public override void OnShown() => Interlocked.Increment(ref _shows);

    public void Count(int count) => Badge = count;
}
