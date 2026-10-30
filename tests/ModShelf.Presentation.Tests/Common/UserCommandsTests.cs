using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.Tests.Common;

public sealed class UserCommandsTests
{
    private readonly List<string> _shown = [];
    private readonly List<Exception> _logged = [];

    [Fact]
    public async Task WorkThatGoesWellShowsAndLogsNothing()
    {
        await RunInPlaceAsync(() => Task.CompletedTask, CancellationToken.None);

        Assert.Empty(_shown);
        Assert.Empty(_logged);
    }

    [Fact]
    public async Task AProblemTheUserCanActOnIsShownAsDescribed()
    {
        await RunInPlaceAsync(() => throw new InvalidDataException("The library can't be read."), CancellationToken.None);

        Assert.Equal(["Described: The library can't be read."], _shown);
        Assert.Empty(_logged);
    }

    [Fact]
    public async Task AnotherProblemIsLoggedAndShownAsSomethingThatWentWrong()
    {
        InvalidOperationException failure = new("A bug.");

        await RunInPlaceAsync(() => throw failure, CancellationToken.None);

        Assert.Equal([Strings.SomethingWentWrong], _shown);
        Assert.Same(failure, Assert.Single(_logged));
    }

    [Fact]
    public async Task CancellingEndsItQuietly()
    {
        using CancellationTokenSource closing = new();
        await closing.CancelAsync();

        await RunInPlaceAsync(() => throw new OperationCanceledException(closing.Token), closing.Token);

        Assert.Empty(_shown);
        Assert.Empty(_logged);
    }

    [Fact]
    public async Task ACancellationNobodyAskedForIsAProblem()
    {
        await RunInPlaceAsync(() => throw new TaskCanceledException(), CancellationToken.None);

        Assert.Equal([Strings.SomethingWentWrong], _shown);
        Assert.IsType<TaskCanceledException>(Assert.Single(_logged));
    }

    private Task RunInPlaceAsync(Func<Task> work, CancellationToken cancellationToken)
        => UserCommands.RunInPlaceAsync(
            work,
            exception => exception is InvalidDataException ? $"Described: {exception.Message}" : null,
            _shown.Add,
            _logged.Add,
            cancellationToken);
}
