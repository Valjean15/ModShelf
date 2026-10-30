using ModShelf.Platform.Secrets;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Secrets;

public sealed class SecretFilesTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task WritingReplacesTheFile()
    {
        string path = Path.Combine(_folder.Path, "account_nexus.bin");
        await File.WriteAllTextAsync(path, "old", Cancellation);

        await SecretFiles.WriteAsync(path, "new"u8.ToArray(), Cancellation);

        Assert.Equal("new", await File.ReadAllTextAsync(path, Cancellation));
        Assert.Single(Directory.EnumerateFiles(_folder.Path));
    }

    [Fact]
    public async Task AWriteThatCantGoInPlaceLeavesNoTemporaryFile()
    {
        // A folder where the file would go.
        string path = Path.Combine(_folder.Path, "account_nexus.bin");
        Directory.CreateDirectory(path);

        Exception? failure = await Record.ExceptionAsync(() => SecretFiles.WriteAsync(path, "new"u8.ToArray(), Cancellation));

        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        Assert.Empty(Directory.EnumerateFiles(_folder.Path));
    }
}
