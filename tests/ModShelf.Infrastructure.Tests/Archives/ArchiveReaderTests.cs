using System.Diagnostics;
using System.IO.Compression;
using System.Text;

using ModShelf.Application.Contents;
using ModShelf.Application.Contents.Ports;
using ModShelf.Infrastructure.Archives;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Archives;

/// <summary>
/// Reading .zip, .7z and .rar archives where they are, extracting nothing (ADR 0106), over the fixtures in
/// Archives/Fixtures and zip archives the tests write.
/// </summary>
public sealed class ArchiveReaderTests : IDisposable
{
    private const string Readme = "Better Lanterns/Readme.txt";
    private const string Notes = "Better Lanterns/Notes/примечания.md";

    private static readonly string ReadmeText = "Better Lanterns 1.2\r\n\r\nPut the .esp in your Data folder.\r\nÜbersetzung: Danke!\r\n";

    private readonly ArchiveReader _reader = new();
    private readonly TempFolder _folder = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData("Mod.zip", "zip")]
    [InlineData("Mod.ZIP", "zip")]
    [InlineData("Mod.7z", "7z")]
    [InlineData("Mod.rar", "rar")]
    [InlineData("Mod.esp", null)]
    [InlineData("Mod.tar.gz", null)]
    [InlineData("zip", null)]
    public void KnowsTheArchivesItReadsByTheirNames(string name, string? format)
    {
        Assert.Equal(format, _reader.FormatOf(name));
    }

    [Theory]
    [InlineData("Lanterns.7z")]
    [InlineData("Lanterns.rar")]
    [InlineData("LockedFiles.7z")]
    [InlineData("LockedFiles.zip")]
    public async Task ListsWhatAnArchiveHoldsWithItsSizesAndNamesInAnyScript(string fixture)
    {
        IReadOnlyList<ArchiveEntry> entries = await _reader.ListAsync(Fixture(fixture), Cancellation);

        ArchiveEntry[] files = [.. entries.Where(entry => !entry.IsFolder).OrderBy(entry => entry.Path, StringComparer.Ordinal)];
        Assert.Equal(
            [
                ("Better Lanterns/Data/BetterLanterns.esp", 1024L),
                ("Better Lanterns/Data/Textures/lantern_d.dds", 2048L),
                (Notes, 41L),
                (Readme, 80L),
            ],
            files.Select(entry => (entry.Path, entry.SizeInBytes)));
        Assert.Contains(entries, entry => entry is { Path: "Better Lanterns/Data", IsFolder: true, SizeInBytes: 0 });
    }

    [Theory]
    [InlineData("Lanterns.7z")]
    [InlineData("Lanterns.rar")]
    public async Task ReadsAFileInAnArchive(string fixture)
    {
        ArchiveBytes readme = await _reader.ReadAsync(Fixture(fixture), Readme, 1024, Cancellation);
        ArchiveBytes notes = await _reader.ReadAsync(Fixture(fixture), Notes, 1024, Cancellation);

        Assert.Equal(ReadmeText, Encoding.UTF8.GetString(readme.Bytes.Span));
        Assert.Equal(80, readme.SizeInBytes);
        Assert.Equal("# Заметки\n\nФонари ярче.\n", Encoding.UTF8.GetString(notes.Bytes.Span));
    }

    [Fact]
    public async Task ReadsAZipArchivesFilesAndOnlyTheStartOfALargeOne()
    {
        string path = WriteZip("Mod.zip", ("Mod/Readme.txt", ReadmeText), ("Mod/Données/été.ini", "[Général]"), ("Mod/Big.txt", new string('x', 5000)));

        IReadOnlyList<ArchiveEntry> entries = await _reader.ListAsync(path, Cancellation);
        ArchiveBytes ini = await _reader.ReadAsync(path, "Mod/Données/été.ini", 1024, Cancellation);
        ArchiveBytes big = await _reader.ReadAsync(path, "Mod/Big.txt", 1024, Cancellation);

        Assert.Equal(["Mod/Readme.txt", "Mod/Données/été.ini", "Mod/Big.txt"], entries.Select(entry => entry.Path));
        Assert.Equal("[Général]", Encoding.UTF8.GetString(ini.Bytes.Span));
        Assert.Equal((1024, 5000L), (big.Bytes.Length, big.SizeInBytes));
    }

    [Fact]
    public async Task ShowsBackslashesAndDotsInEntriesPathsAsFoldersNeverAsPathsOnDisk()
    {
        string path = WriteZip("Odd.zip", ("Mod\\Data\\a.esp", "a"), ("./Mod/b.txt", "b"), ("../../outside.txt", "c"));

        IReadOnlyList<ArchiveEntry> entries = await _reader.ListAsync(path, Cancellation);
        ArchiveBytes outside = await _reader.ReadAsync(path, "../../outside.txt", 16, Cancellation);

        Assert.Equal(["Mod/Data/a.esp", "Mod/b.txt", "../../outside.txt"], entries.Select(entry => entry.Path));
        Assert.Equal("c", Encoding.UTF8.GetString(outside.Bytes.Span));
        Assert.Equal(["Odd.zip"], Directory.GetFileSystemEntries(_folder.Path).Select(Path.GetFileName));
        Assert.False(File.Exists(Path.Combine(_folder.Path, "..", "..", "outside.txt")));
    }

    [Theory]
    [InlineData("Locked.7z")]
    [InlineData("Locked.rar")]
    public async Task AnArchiveWhoseListNeedsAPasswordIsEncrypted(string fixture)
    {
        ArchiveUnreadableException refused = await Assert.ThrowsAsync<ArchiveUnreadableException>(() => _reader.ListAsync(Fixture(fixture), Cancellation));

        Assert.Equal(ArchiveTrouble.Encrypted, refused.Trouble);
        Assert.Equal($"{fixture} needs a password, which ModShelf doesn't ask for.", refused.Message);
    }

    [Theory]
    [InlineData("LockedFiles.7z")]
    [InlineData("LockedFiles.zip")]
    public async Task AFileThatNeedsAPasswordIsEncrypted(string fixture)
    {
        ArchiveUnreadableException refused = await Assert.ThrowsAsync<ArchiveUnreadableException>(
            () => _reader.ReadAsync(Fixture(fixture), Readme, 1024, Cancellation));

        Assert.Equal(ArchiveTrouble.Encrypted, refused.Trouble);
        Assert.Equal($"{Readme} in {fixture} needs a password, which ModShelf doesn't ask for.", refused.Message);
    }

    [Theory]
    [InlineData("Lanterns.7z", "7z")]
    [InlineData("Lanterns.rar", "rar")]
    [InlineData("LockedFiles.zip", "zip")]
    public async Task AnArchiveCutShortIsBroken(string fixture, string format)
    {
        byte[] whole = await File.ReadAllBytesAsync(Fixture(fixture), Cancellation);
        string cut = Path.Combine(_folder.Path, fixture);
        await File.WriteAllBytesAsync(cut, whole[..(whole.Length / 3)], Cancellation);

        ArchiveUnreadableException refused = await Assert.ThrowsAsync<ArchiveUnreadableException>(async () =>
        {
            await _reader.ListAsync(cut, Cancellation);
            await _reader.ReadAsync(cut, Readme, 1024, Cancellation);
        });

        Assert.Equal(ArchiveTrouble.Broken, refused.Trouble);
        Assert.Equal($"{fixture} can't be read as a {format} archive: it's damaged, cut short, or isn't one.", refused.Message);
    }

    [Theory]
    [InlineData("Fake.zip")]
    [InlineData("Fake.7z")]
    [InlineData("Fake.rar")]
    public async Task AFileThatIsntWhatItsNameSaysIsBroken(string name)
    {
        string path = Path.Combine(_folder.Path, name);
        await File.WriteAllTextAsync(path, "This is a text file, not an archive at all.", Cancellation);

        ArchiveUnreadableException refused = await Assert.ThrowsAsync<ArchiveUnreadableException>(() => _reader.ListAsync(path, Cancellation));

        Assert.Equal(ArchiveTrouble.Broken, refused.Trouble);
    }

    [Fact]
    public async Task RefusesAFileThatIsntAnArchiveItReadsAndAnEntryItDoesntHave()
    {
        string path = WriteZip("Mod.zip", ("Mod/Readme.txt", ReadmeText));

        ArchiveUnreadableException esp = await Assert.ThrowsAsync<ArchiveUnreadableException>(
            () => _reader.ListAsync(Path.Combine(_folder.Path, "Mod.esp"), Cancellation));
        KeyNotFoundException missing = await Assert.ThrowsAsync<KeyNotFoundException>(() => _reader.ReadAsync(path, "Mod/Other.txt", 16, Cancellation));
        KeyNotFoundException folder = await Assert.ThrowsAsync<KeyNotFoundException>(() => _reader.ReadAsync(Fixture("Lanterns.7z"), "Better Lanterns/Data", 16, Cancellation));

        Assert.Equal(ArchiveTrouble.NotAnArchive, esp.Trouble);
        Assert.Equal("Mod.zip has no file Mod/Other.txt.", missing.Message);
        Assert.Equal("Lanterns.7z has no file Better Lanterns/Data.", folder.Message);
    }

    [Fact]
    public async Task ListsAHundredThousandEntriesQuickly()
    {
        string path = Path.Combine(_folder.Path, "Many.zip");
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            for (int index = 0; index < 100_000; index++)
            {
                archive.CreateEntry($"Mod/Folder {index / 1000}/File {index}.dds", CompressionLevel.NoCompression);
            }
        }

        Stopwatch listing = Stopwatch.StartNew();
        IReadOnlyList<ArchiveEntry> entries = await _reader.ListAsync(path, Cancellation);
        listing.Stop();

        Assert.Equal(100_000, entries.Count);
        Assert.InRange(listing.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task StopsWhenAskedTo()
    {
        using CancellationTokenSource stop = new();
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _reader.ListAsync(Fixture("Lanterns.7z"), stop.Token));
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Archives", "Fixtures", name);

    private string WriteZip(string name, params (string Path, string Text)[] files)
    {
        string path = Path.Combine(_folder.Path, name);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string entry, string text) in files)
        {
            using StreamWriter writer = new(archive.CreateEntry(entry).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(text);
        }

        return path;
    }
}
