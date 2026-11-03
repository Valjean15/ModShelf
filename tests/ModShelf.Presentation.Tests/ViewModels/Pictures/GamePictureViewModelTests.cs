using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.Tests.ViewModels.Pictures;

public sealed class GamePictureViewModelTests
{
    private static readonly Uri Banner = new("https://images.nexusmods.com/images/games/v2/1704/skyrim-hero.jpg");
    private static readonly Uri Other = new("https://images.nexusmods.com/images/games/v2/1704/other-hero.jpg");

    private readonly TestPictures _pictures = new();
    private readonly GamePictureViewModel _cover;

    public GamePictureViewModelTests() => _cover = new GamePictureViewModel(_pictures.Pictures);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShowsThePictureItsSiteGivesOnceShown()
    {
        _cover.Place(Banner, Cancellation);
        string? before = _cover.File;
        IReadOnlyList<Uri> askedBefore = _pictures.Cache.Asked;

        _cover.Show(Cancellation);
        await _cover.Showing;

        Assert.Null(before);
        Assert.Empty(askedBefore);
        Assert.Equal(ScriptedPictureCache.FileOf(Banner), _cover.File);
        Assert.True(_cover.HasFile);
    }

    [Fact]
    public async Task APicturePlacedWhileShownShowsAtOnce()
    {
        _cover.Show(Cancellation);
        await _cover.Showing;
        bool before = _cover.HasFile;

        _cover.Place(Banner, Cancellation);
        await _cover.Showing;

        Assert.False(before);
        Assert.Equal(ScriptedPictureCache.FileOf(Banner), _cover.File);
    }

    [Fact]
    public async Task ReadsAPictureOnce()
    {
        _cover.Show(Cancellation);
        _cover.Place(Banner, Cancellation);
        await _cover.Showing;

        _cover.Show(Cancellation);
        _cover.Place(Banner, Cancellation);
        await _cover.Showing;

        Assert.Equal([Banner], _pictures.Cache.Asked);
        Assert.Equal(ScriptedPictureCache.FileOf(Banner), _cover.File);
    }

    [Fact]
    public async Task WaitingForAPictureBeingReadWaitsForThatRead()
    {
        _pictures.Cache.Held = new TaskCompletionSource();
        _cover.Show(Cancellation);
        _cover.Place(Banner, Cancellation);

        _cover.Show(Cancellation);
        Task showing = _cover.Showing;
        bool doneEarly = showing.IsCompleted;
        _pictures.Cache.Held.SetResult();
        await showing;

        Assert.False(doneEarly);
        Assert.Equal(ScriptedPictureCache.FileOf(Banner), _cover.File);
    }

    [Fact]
    public async Task ShowsNoneWhenTheSiteGivesNoneOrItCantBeDownloaded()
    {
        GamePictureViewModel missing = new(_pictures.Pictures);
        _pictures.Cache.Missing[Banner] = true;

        _cover.Show(Cancellation);
        _cover.Place(null, Cancellation);
        await _cover.Showing;
        missing.Show(Cancellation);
        missing.Place(Banner, Cancellation);
        await missing.Showing;

        Assert.Null(_cover.File);
        Assert.False(_cover.HasFile);
        Assert.Null(missing.File);
        Assert.Equal([Banner], _pictures.Cache.Asked);
    }

    [Fact]
    public async Task APictureNoLongerGivenIsNoLongerShown()
    {
        _cover.Show(Cancellation);
        _cover.Place(Banner, Cancellation);
        await _cover.Showing;

        _cover.Place(null, Cancellation);
        await _cover.Showing;

        Assert.Null(_cover.File);
    }

    [Fact]
    public async Task ANewerPictureReplacesOneBeingRead()
    {
        _pictures.Cache.Held = new TaskCompletionSource();
        _cover.Show(Cancellation);
        _cover.Place(Banner, Cancellation);
        Task first = _cover.Showing;

        _cover.Place(Other, Cancellation);
        _pictures.Cache.Held.SetResult();
        await Task.WhenAll(first, _cover.Showing);

        Assert.Equal(ScriptedPictureCache.FileOf(Other), _cover.File);
        Assert.Equal([Banner, Other], _pictures.Cache.Asked);
    }

    [Fact]
    public async Task HidingShowsNoneForgettingOneBeingReadAndShowingReadsItAgain()
    {
        _pictures.Cache.Held = new TaskCompletionSource();
        _cover.Show(Cancellation);
        _cover.Place(Banner, Cancellation);
        Task reading = _cover.Showing;

        _cover.Hide();
        bool hiddenDone = _cover.Showing.IsCompleted;
        _pictures.Cache.Held.SetResult();
        await reading;
        string? hidden = _cover.File;
        _cover.Place(Banner, Cancellation);
        string? placedWhileHidden = _cover.File;
        _cover.Show(Cancellation);
        await _cover.Showing;

        Assert.Null(hidden);
        Assert.True(hiddenDone);
        Assert.Null(placedWhileHidden);
        Assert.Equal(ScriptedPictureCache.FileOf(Banner), _cover.File);
        Assert.Equal([Banner, Banner], _pictures.Cache.Asked);
    }

    [Fact]
    public async Task HidingAPictureShownShowsNone()
    {
        _cover.Show(Cancellation);
        _cover.Place(Banner, Cancellation);
        await _cover.Showing;

        _cover.Hide();

        Assert.Null(_cover.File);
        Assert.False(_cover.HasFile);
    }

    [Fact]
    public async Task AFailureIsLoggedAndShowsNone()
    {
        _pictures.Cache.Failure = new InvalidOperationException("Something broke.");

        _cover.Show(Cancellation);
        _cover.Place(Banner, Cancellation);
        await _cover.Showing;

        Assert.Null(_cover.File);
        string logged = Assert.Single(_pictures.Logger.Entries);
        Assert.StartsWith("Error: A game's picture couldn't be shown", logged, StringComparison.Ordinal);
        Assert.Contains("Something broke.", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoppingWhileReadingIsntLogged()
    {
        using CancellationTokenSource stopping = new();
        _pictures.Cache.Held = new TaskCompletionSource();
        _cover.Show(stopping.Token);
        _cover.Place(Banner, stopping.Token);

        await stopping.CancelAsync();
        await _cover.Showing;

        Assert.Null(_cover.File);
        Assert.Empty(_pictures.Logger.Entries);
    }

    [Fact]
    public async Task AFailureNotFromStoppingIsLoggedEvenWhenItsACancellation()
    {
        _pictures.Cache.Failure = new OperationCanceledException("Timed out.");

        _cover.Show(Cancellation);
        _cover.Place(Banner, Cancellation);
        await _cover.Showing;

        Assert.Contains("Timed out.", Assert.Single(_pictures.Logger.Entries), StringComparison.Ordinal);
    }
}
