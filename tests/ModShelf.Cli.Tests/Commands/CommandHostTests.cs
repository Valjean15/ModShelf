using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Cli.Commands;
using ModShelf.Cli.Hosting;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// A command's host is started for its work, and stopped however the work ends.
/// </summary>
public sealed class CommandHostTests : IDisposable
{
    private readonly RecordingHost _host = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task StopsTheHostOnceTheWorkIsDone()
    {
        int exitCode = await CommandHost.RunAsync(Environment(), _ => Task.FromResult(3), Cancellation);

        Assert.Equal(3, exitCode);
        Assert.Equal(["start", "stop", "dispose"], _host.Calls);
    }

    [Fact]
    public async Task StopsTheHostWhenTheWorkFails()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CommandHost.RunAsync(Environment(), _ => throw new InvalidOperationException("No."), Cancellation));

        Assert.Equal(["start", "stop", "dispose"], _host.Calls);
    }

    [Fact]
    public async Task BuildsAHostThatDoesntRunTheDownloads()
    {
        bool? runDownloads = null;

        await CommandHost.RunAsync(Environment(downloads => runDownloads = downloads), _ => Task.FromResult(0), Cancellation);

        Assert.False(runDownloads);
    }

    private CliEnvironment Environment(Action<bool>? built = null)
        => new(TextReader.Null, TextWriter.Null, TextWriter.Null, InputIsTerminal: false, OutputIsTerminal: false, runDownloads =>
        {
            built?.Invoke(runDownloads);
            return _host;
        });

    private sealed class RecordingHost : IHost
    {
        private readonly List<string> _calls = [];

        public IServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();

        public IReadOnlyList<string> Calls => _calls;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _calls.Add("start");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            _calls.Add("stop");
            return Task.CompletedTask;
        }

        public void Dispose() => _calls.Add("dispose");
    }
}
