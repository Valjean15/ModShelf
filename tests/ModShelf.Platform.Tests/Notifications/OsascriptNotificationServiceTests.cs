using System.ComponentModel;
using System.Diagnostics;

using ModShelf.Application.Notifications.Ports;
using ModShelf.Platform.Notifications;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Notifications;

/// <summary>
/// <c>osascript</c> as the service starts it, with the process replaced, so these run on every system.
/// </summary>
public sealed class OsascriptNotificationServiceTests
{
    private static readonly Notification Notice = new("2 new updates", "\"SkyUI\" & SMAPI");

    private readonly RecordingLogger<OsascriptNotificationServiceTests> _logger = new();
    private readonly List<ProcessStartInfo> _started = [];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PassesTheTitleAndMessageAsTheScriptsArgumentsWithoutAShell()
    {
        await Service(new ProcessResult(0, string.Empty)).ShowAsync(Notice, Cancellation);

        ProcessStartInfo started = Assert.Single(_started);
        Assert.Equal("/usr/bin/osascript", started.FileName);
        Assert.False(started.UseShellExecute);
        Assert.Empty(started.Arguments);
        Assert.Equal(
            ["-e", "on run argv", "-e", "display notification (item 2 of argv) with title (item 1 of argv)", "-e", "end run", "2 new updates", "\"SkyUI\" & SMAPI"],
            started.ArgumentList);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task LogsWhatOsascriptSaidWhenItFails()
    {
        await Service(new ProcessResult(1, "execution error: Not allowed. (-1743)\n")).ShowAsync(Notice, Cancellation);

        string entry = Assert.Single(_logger.Entries);
        Assert.StartsWith("Warning: Couldn't show the notice \"2 new updates\"", entry, StringComparison.Ordinal);
        Assert.Contains("osascript stopped with exit code 1: execution error: Not allowed. (-1743)", entry, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogsAnOsascriptThatWontStartOrFinish(bool starts)
    {
        Exception failure = starts ? new TimeoutException("osascript didn't finish.") : new Win32Exception(2, "No such file.");
        OsascriptNotificationService service = new((_, _) => Task.FromException<ProcessResult>(failure), _logger);

        await service.ShowAsync(Notice, Cancellation);

        Assert.StartsWith("Warning: Couldn't show the notice \"2 new updates\"", Assert.Single(_logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancelledNoticeIsCancelledNotLogged()
    {
        OsascriptNotificationService service = new((_, token) => Task.FromCanceled<ProcessResult>(token), _logger);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ShowAsync(Notice, cancelled.Token));

        Assert.Empty(_logger.Entries);
    }

    private OsascriptNotificationService Service(ProcessResult result) => new(
        (info, _) =>
        {
            _started.Add(info);
            return Task.FromResult(result);
        },
        _logger);
}
