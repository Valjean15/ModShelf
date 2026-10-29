using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;

using ModShelf.Application.Desktop;
using ModShelf.Platform.Instances;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Instances;

/// <summary>
/// One desktop app per library folder: a second launch hands its arguments to the one running, over the named pipe, in
/// this process as from another.
/// </summary>
public sealed class PipeSingleInstanceTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly TempFolder _folder = new();
    private readonly RecordingLogger<PipeSingleInstance> _logger = new();
    private readonly List<PipeSingleInstance> _instances = [];
    private readonly BlockingCollection<Launch> _launches = [];

    public void Dispose()
    {
        foreach (PipeSingleInstance instance in _instances)
        {
            instance.Dispose();
        }

        _launches.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public void TheFirstLaunchIsTheAppAndASecondHandsItsArgumentsOver()
    {
        PipeSingleInstance running = Listening(Instance());

        bool claimed = Instance().TryClaim(["--minimized", "nxm://stardewvalley/mods/2400/files/1?key=secret-key"]);

        Assert.False(claimed);
        Assert.Equal(["--minimized", "nxm://stardewvalley/mods/2400/files/1?key=secret-key"], NextLaunch().Arguments);
        Assert.True(running.TryClaim([]));
    }

    [Fact]
    public void EachLaunchIsHeardInTurn()
    {
        Listening(Instance());

        Assert.False(Instance().TryClaim(["one"]));
        Assert.False(Instance().TryClaim(["two"]));
        Assert.False(Instance().TryClaim([]));

        Assert.Equal(["one"], NextLaunch().Arguments);
        Assert.Equal(["two"], NextLaunch().Arguments);
        Assert.Empty(NextLaunch().Arguments);
    }

    [Fact]
    public void AnotherLibrarysAppRunsBesideIt()
    {
        using TempFolder other = new();
        Listening(Instance());

        Assert.True(Instance(other.Path).TryClaim([]));
        Assert.NotEqual(PipeSingleInstance.PipeNameFor(_folder.Path), PipeSingleInstance.PipeNameFor(other.Path));
    }

    [Fact]
    public void OnceTheAppEndsTheNextLaunchIsTheApp()
    {
        PipeSingleInstance first = Instance();
        Assert.True(first.TryClaim([]));

        first.Dispose();
        PipeSingleInstance next = Listening(Instance());

        Assert.True(next.TryClaim([]));
        Assert.False(Instance().TryClaim(["again"]));
        Assert.Equal(["again"], NextLaunch().Arguments);
    }

    [Fact]
    public async Task AnAppEndingWhileItShowsItselfFinishesFirstSoTheNextAppCanListen()
    {
        using ManualResetEventSlim showing = new();
        using ManualResetEventSlim shown = new();
        PipeSingleInstance first = Instance();
        first.LaunchedAgain += (_, _) =>
        {
            showing.Set();
            shown.Wait(Patience, TestContext.Current.CancellationToken);
        };
        Assert.True(first.TryClaim([]));
        PipeSingleInstance second = Instance();
        Task<bool> launch = Task.Run(() => second.TryClaim(["one"]), TestContext.Current.CancellationToken);
        Assert.True(showing.Wait(Patience, TestContext.Current.CancellationToken), "The launch wasn't handed over.");

        Task ending = Task.Run(first.Dispose, TestContext.Current.CancellationToken);
        Task sooner = await Task.WhenAny(ending, Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken));
        shown.Set();
        await ending.WaitAsync(Patience, TestContext.Current.CancellationToken);

        // Ending, the app may not answer that launch; either way, the launch is over.
        await Task.WhenAny(launch, Task.Delay(Patience, TestContext.Current.CancellationToken));
        Assert.True(launch.IsCompleted);
        Assert.NotSame(ending, sooner);
        Listening(Instance());
        Assert.False(Instance().TryClaim(["two"]));
        Assert.Equal(["two"], NextLaunch().Arguments);
    }

    [Fact]
    public void AnAppThatHoldsTheLockButDoesntAnswerTimesOut()
    {
        // Such as an app that hangs: the lock is held, and nothing listens.
        using FileStream held = new(Path.Combine(_folder.Path, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        TimeoutException refused = Assert.Throws<TimeoutException>(() => Instance(patience: TimeSpan.FromMilliseconds(300)).TryClaim([]));

        Assert.Equal("ModShelf is running already, but didn't answer.", refused.Message);
    }

    [Fact]
    public void AnAppThatHearsButNeverAnswersTimesOut()
    {
        using FileStream held = new(Path.Combine(_folder.Path, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using NamedPipeServerStream silent = new(
            PipeSingleInstance.PipeNameFor(_folder.Path),
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Task accepted = silent.WaitForConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Throws<TimeoutException>(() => Instance(patience: TimeSpan.FromMilliseconds(500)).TryClaim([]));
        Assert.True(accepted.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AnAppThatHangsUpWithoutAnsweringTimesOut()
    {
        // Such as another program on the pipe's name: it reads the launch, then closes.
        using FileStream held = new(Path.Combine(_folder.Path, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await using NamedPipeServerStream rude = new(
            PipeSingleInstance.PipeNameFor(_folder.Path),
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Task hangsUp = Task.Run(
            async () =>
            {
                await rude.WaitForConnectionAsync(TestContext.Current.CancellationToken);
                using StreamReader reader = new(rude, leaveOpen: true);
                await reader.ReadLineAsync(TestContext.Current.CancellationToken);
                rude.Disconnect();
            },
            TestContext.Current.CancellationToken);

        Assert.Throws<TimeoutException>(() => Instance().TryClaim(["hello"]));
        await hangsUp.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AMessageTooLongIsntReadAndTheNextLaunchIsHeard()
    {
        Listening(Instance());

        await using (NamedPipeClientStream stranger = await StrangerAsync())
        {
            // Arguments of 70,000 characters, on one line.
            byte[] message = Encoding.UTF8.GetBytes("{\"Arguments\": [\"" + new string('x', 70_000) + "\"]}\n");
            try
            {
                await stranger.WriteAsync(message, TestContext.Current.CancellationToken);
            }
            catch (IOException)
            {
                // It may hang up before it has read it all.
            }

            // It hangs up without an answer: the end of the stream, or a reset, as it left the rest unread.
            int answer;
            try
            {
                answer = await FirstByteOfTheAnswerAsync(stranger);
            }
            catch (IOException)
            {
                answer = -1;
            }

            Assert.Equal(-1, answer);
        }

        Assert.False(Instance().TryClaim(["after"]));

        Assert.Equal(["after"], NextLaunch().Arguments);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: A launch's message couldn't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAppWhoseHandlerFailsStillAnswersAndHearsTheNextLaunch()
    {
        PipeSingleInstance running = Listening(Instance());
        running.LaunchedAgain += (_, _) => throw new InvalidOperationException("The window is gone.");

        Assert.False(Instance().TryClaim(["first"]));
        Assert.False(Instance().TryClaim(["second"]));

        Assert.Equal(["first"], NextLaunch().Arguments);
        Assert.Equal(["second"], NextLaunch().Arguments);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Error: The app failed while showing itself for another launch", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not json\n")]
    [InlineData("{\"Arguments\": null}\n")]
    [InlineData("\n")]
    public async Task AMessageItCantReadIsLoggedAndTheNextLaunchIsHeard(string message)
    {
        Listening(Instance());

        await using (NamedPipeClientStream stranger = await StrangerAsync())
        {
            await stranger.WriteAsync(Encoding.UTF8.GetBytes(message), TestContext.Current.CancellationToken);
            await stranger.FlushAsync(TestContext.Current.CancellationToken);

            // It hangs up without an answer.
            Assert.Equal(-1, await FirstByteOfTheAnswerAsync(stranger));
        }

        Assert.False(Instance().TryClaim(["after"]));

        Assert.Equal(["after"], NextLaunch().Arguments);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: A launch's message couldn't be read", StringComparison.Ordinal));
    }

    [Fact]
    public void ALaunchsArgumentsAreNeverLogged()
    {
        Listening(Instance());

        Instance().TryClaim(["nxm://stardewvalley/mods/2400/files/1?key=secret-key&expires=1"]);
        NextLaunch();

        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: ModShelf was launched again", StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Contains("secret-key", StringComparison.Ordinal) || entry.Contains("nxm", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePipesNameIsTheFoldersWhateverItsEndingOrCase()
    {
        string name = PipeSingleInstance.PipeNameFor(_folder.Path);

        Assert.Equal(name, PipeSingleInstance.PipeNameFor(_folder.Path + Path.DirectorySeparatorChar));
        // Linux's file systems tell case apart; Windows' and macOS's usual ones don't.
        Assert.Equal(!OperatingSystem.IsLinux(), name == PipeSingleInstance.PipeNameFor(_folder.Path.ToUpperInvariant()));
        Assert.Matches("^ModShelf-[0-9A-F]{24}$", name);
    }

    [Fact]
    public void ADisposedInstanceCantClaim()
    {
        PipeSingleInstance instance = Instance();
        instance.Dispose();

        Assert.Throws<ObjectDisposedException>(() => instance.TryClaim([]));
        instance.Dispose();
    }

    [Fact]
    public void AFolderThatIsntAFullPathIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new PipeSingleInstance("Library", Patience, _logger));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipeSingleInstance(_folder.Path, TimeSpan.Zero, _logger));
    }

    private PipeSingleInstance Instance(string? folder = null, TimeSpan? patience = null)
    {
        PipeSingleInstance instance = new(folder ?? _folder.Path, patience ?? Patience, _logger);
        _instances.Add(instance);
        return instance;
    }

    /// <summary>
    /// Makes <paramref name="instance"/> the app, recording the launches handed to it.
    /// </summary>
    private PipeSingleInstance Listening(PipeSingleInstance instance)
    {
        instance.LaunchedAgain += (_, launch) => _launches.Add(launch);
        Assert.True(instance.TryClaim([]));
        return instance;
    }

    private Launch NextLaunch()
    {
        Assert.True(_launches.TryTake(out Launch? launch, Patience), "No launch was handed over.");
        return launch!;
    }

    /// <summary>
    /// Another program on the app's pipe, connected to it; a test fails, rather than waits, when nothing listens.
    /// </summary>
    private async Task<NamedPipeClientStream> StrangerAsync()
    {
        NamedPipeClientStream stranger = new(
            ".",
            PipeSingleInstance.PipeNameFor(_folder.Path),
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await stranger.ConnectAsync(Patience, TestContext.Current.CancellationToken);
            return stranger;
        }
        catch
        {
            await stranger.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The first byte the app answers, or -1 when it hangs up; a test fails, rather than waits, when it does neither.
    /// </summary>
    private static async Task<int> FirstByteOfTheAnswerAsync(NamedPipeClientStream stranger)
    {
        byte[] answer = new byte[1];
        int read = await stranger.ReadAsync(answer, TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(Patience, TestContext.Current.CancellationToken);
        return read == 0 ? -1 : answer[0];
    }
}
