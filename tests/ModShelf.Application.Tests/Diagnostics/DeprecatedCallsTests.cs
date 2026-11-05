using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Diagnostics;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Diagnostics;

/// <summary>
/// Counting the calls that use a deprecated argument: what's kept, and a count that can't be kept never failing the call.
/// </summary>
public sealed class DeprecatedCallsTests
{
    private readonly MemoryDeprecatedCallStore _store = new();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly RecordingLogger<DeprecatedCalls> _logger = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CountsEachCallTrimmedAtTheTimeItsMade()
    {
        DeprecatedCalls calls = Calls();

        await calls.CountAsync(" download.link ", " DL_LINK_ARG ", " claude-ai ", Cancellation);
        _time.Advance(TimeSpan.FromHours(1));
        await calls.CountAsync("download.link", "DL_LINK_ARG", null, Cancellation);

        Assert.Equal(
            [("download.link", "DL_LINK_ARG", "claude-ai", TestData.Now), ("download.link", "DL_LINK_ARG", null, TestData.Now.AddHours(1))],
            _store.Added);
        Assert.Equal([new DeprecatedCall("download.link", "DL_LINK_ARG", 2, TestData.Now, TestData.Now.AddHours(1), null)], await calls.ListAsync(Cancellation));
        Assert.Empty(_logger.Entries);
    }

    [Theory]
    [InlineData(" ", "DL_LINK_ARG")]
    [InlineData("download.link", "")]
    public async Task ABlankNameOrCodeIsRefused(string name, string code)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Calls().CountAsync(name, code, null, Cancellation));

        Assert.Empty(_store.Added);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task ACountThatCantBeKeptIsLoggedAndTheCallGoesOn(Type failure)
    {
        _store.Failure = (Exception)Activator.CreateInstance(failure, "The file is locked.")!;

        await Calls().CountAsync("download.link", " DL_LINK_ARG ", "claude-ai", Cancellation);

        string logged = Assert.Single(_logger.Entries);
        Assert.StartsWith("Warning: A call with the deprecated DL_LINK_ARG couldn't be counted " + failure.FullName, logged, StringComparison.Ordinal);
        Assert.Contains("The file is locked.", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherFailuresAreNotHidden()
    {
        _store.Failure = new InvalidOperationException("A bug.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Calls().CountAsync("download.link", "DL_LINK_ARG", null, Cancellation));
    }

    [Fact]
    public async Task KeepsTheClientsFirstLineAndNotTooMuchOfIt()
    {
        string longName = new('c', DeprecatedCalls.LongestClient + 10);

        await Calls().CountAsync("download.link", "DL_LINK_ARG", "first\tline\r\nsecond line", Cancellation);
        await Calls().CountAsync("download.link", "DL_LINK_ARG", longName, Cancellation);
        await Calls().CountAsync("download.link", "DL_LINK_ARG", "\n", Cancellation);

        Assert.Equal(["first line", longName[..DeprecatedCalls.LongestClient], null], _store.Added.Select(added => added.Client));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  \t ", null)]
    [InlineData(" ab ", "ab")]
    [InlineData("one\ntwo", "one")]
    [InlineData("one\rtwo", "one")]
    [InlineData("a\u0007b", "a b")]
    [InlineData("abcdef", "abcd")]
    [InlineData("abc def", "abc")]
    public void OneLineIsTheFirstLineCleanAndShort(string? text, string? expected)
        => Assert.Equal(expected, DeprecatedCalls.OneLine(text, 4));

    [Theory]
    [InlineData("first\rsecond")]
    [InlineData("first\nsecond")]
    [InlineData("first\r\nsecond")]
    public void OneLineEndsAtTheFirstLineBreakOfAnyKind(string text)
        => Assert.Equal("first", DeprecatedCalls.OneLine(text, DeprecatedCalls.LongestClient));

    [Fact]
    public void OneLineNeverCutsACharacterMadeOfTwoInHalf()
        => Assert.Equal("abc", DeprecatedCalls.OneLine("abc\U0001F600", 4));

    private DeprecatedCalls Calls() => new(_store, _time, _logger);
}
