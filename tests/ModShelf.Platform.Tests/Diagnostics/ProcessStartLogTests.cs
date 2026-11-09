using System.Diagnostics;

using ModShelf.Application.Diagnostics;
using ModShelf.Platform.Diagnostics;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Diagnostics;

/// <summary>
/// The line each process's logs start with: which ModShelf, what it runs, its process and the one that started it.
/// </summary>
public sealed class ProcessStartLogTests
{
    [Fact]
    public async Task ItSaysWhatRunsWhereAndStartedByWhat()
    {
        RecordingLogger<ProcessStartLog> logger = new();
        ProcessStartLog line = new("the desktop app", new AppInfo("1.0.1", "abc1234"), logger);
        int parent = Assert.NotNull(ParentProcess.FindId());
        using Process parentProcess = Process.GetProcessById(parent);

        await line.StartingAsync(TestContext.Current.CancellationToken);
        await line.StartAsync(TestContext.Current.CancellationToken);
        await line.StartedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            $"Information: ModShelf 1.0.1 (abc1234): the desktop app, process {Environment.ProcessId}, started by {parentProcess.ProcessName} (process {parent}) ",
            Assert.Single(logger.Entries));
    }
}
