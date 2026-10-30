using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

using ModShelf.Cli.Hosting;
using ModShelf.Testing;
using ModShelf.Hosting;

namespace ModShelf.Cli.Tests.Hosting;

/// <summary>
/// What the CLI's console shows of a log entry: one line, without the stack trace the log files keep.
/// </summary>
public sealed class CliConsoleFormatterTests
{
    [Fact]
    public void AnEntryWithAnExceptionIsOneLineWithoutItsStackTrace()
    {
        HttpRequestException failed;
        try
        {
            throw new HttpRequestException("Name or service not known (host.invalid:443)");
        }
        catch (HttpRequestException exception)
        {
            failed = exception;
        }

        string written = Write(LogLevel.Warning, "Couldn't reach host.invalid", failed);

        Assert.Equal("warn: Couldn't reach host.invalid" + Environment.NewLine, written);
    }

    [Theory]
    [InlineData(LogLevel.Information, "info")]
    [InlineData(LogLevel.Error, "fail")]
    [InlineData(LogLevel.Critical, "crit")]
    public void EachLevelHasTheConsolesShortName(LogLevel level, string name)
    {
        Assert.Equal($"{name}: Line one line two" + Environment.NewLine, Write(level, "Line one\nline two", exception: null));
    }

    [Fact]
    public void TheCliHostUsesIt()
    {
        using TempFolder folder = new();
        using IHost host = CliHost.Build(
            runDownloads: false,
            new Dictionary<string, string?> { [HostServices.DatabasePathKey] = Path.Combine(folder.Path, "library.db") });

        Assert.Equal(
            CliConsoleFormatter.FormatterName,
            host.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName);
        Assert.Contains(host.Services.GetServices<ConsoleFormatter>(), formatter => formatter is CliConsoleFormatter);
    }

    private static string Write(LogLevel level, string message, Exception? exception)
    {
        using StringWriter written = new();
        new CliConsoleFormatter().Write(
            new LogEntry<string>(level, "ModShelf.Infrastructure.Downloads.HttpFileTransfer", new EventId(102), message, exception, (state, _) => state),
            scopeProvider: null,
            written);
        return written.ToString();
    }
}
