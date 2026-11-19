using System.Text;

using ModShelf.Application.Contents;
using ModShelf.Application.Settings;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Contents;

/// <summary>
/// What the library's archives hold, listed and read through a scripted reader (ADR 0106).
/// </summary>
public sealed class LibraryContentsTests : IDisposable
{
    private const string Folder = "nexus/skyrimspecialedition/SkyUI (12604)";

    private static readonly ModRef SkyUi = new(new GameRef(TestData.Nexus, "skyrimspecialedition"), "12604");
    private static readonly ModFileRef Archive = new(SkyUi, "35408");
    private static readonly ModFileRef Plugin = new(SkyUi, "35409");

    private readonly ListedLibrary _library = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly InMemoryLibraryRepository _repository = new();
    private readonly FakeLibraryStorage _storage = new();
    private readonly ScriptedArchiveReader _reader = new();
    private readonly RecordingLogger<LibraryContents> _logger = new();
    private readonly SettingsService _settings;
    private readonly LibraryContents _contents;

    public LibraryContentsTests()
    {
        _settings = new SettingsService(_store, new FakeUserFolders(), _repository, new InMemoryDownloadJobRepository([]));
        _contents = new LibraryContents(_library, _settings, _storage, _reader, _logger);
        _library.Mods.Add(LibraryMod.Watch(SkyUi, "SkyUI", "schlangster", new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/12604"), TestData.Now)
            .WithFile(File(Archive, $"{Folder}/SkyUI_5_2_SE.7z"))
            .WithFile(File(Plugin, $"{Folder}/SkyUI.esp")));
        _storage.Add(ArchivePath);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string ArchivePath => LibraryPaths.Resolve(_settings.DefaultLibraryRoot, $"{Folder}/SkyUI_5_2_SE.7z");

    public void Dispose() => _settings.Dispose();

    [Fact]
    public async Task ListsEveryFolderOnceFoldersFirstWithTheFilesCountAndSize()
    {
        _reader.Entries[ArchivePath] =
        [
            new("SkyUI/Readme.txt", 120, IsFolder: false),
            new("SkyUI\\Interface\\skyui\\icons.swf", 3000, IsFolder: false),
            new("SkyUI/Interface/", 0, IsFolder: true),
            new("./SkyUI/SkyUI_SE.bsa", 900, IsFolder: false),
            new("SkyUI/interface/Translations/english.txt", 80, IsFolder: false),
        ];

        ArchiveContents contents = await _contents.ListAsync(Archive, Cancellation);

        Assert.Equal(
            [
                "SkyUI/",
                "SkyUI/Interface/",
                "SkyUI/Interface/skyui/",
                "SkyUI/Interface/skyui/icons.swf",
                "SkyUI/interface/",
                "SkyUI/interface/Translations/",
                "SkyUI/interface/Translations/english.txt",
                "SkyUI/Readme.txt",
                "SkyUI/SkyUI_SE.bsa",
            ],
            contents.Entries.Select(entry => entry.IsFolder ? entry.Path + "/" : entry.Path));
        Assert.Equal(("SkyUI_5_2_SE.7z", "7z", 4, 4100L), (contents.FileName, contents.Format, contents.Files, contents.UnpackedSize));
        Assert.Equal("SkyUI/Readme.txt", contents.Readme);
        Assert.Contains(_logger.Entries, entry => entry.Contains("Listed 4 files in 5 folders of SkyUI_5_2_SE.7z", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(new[] { "Docs/Readme.md", "readme.txt" }, "readme.txt")]
    [InlineData(new[] { "Mod/SkyUI - Read Me.txt", "Mod/Changes.txt" }, "Mod/SkyUI - Read Me.txt")]
    [InlineData(new[] { "Mod/Read_Me.pdf", "Mod/Readme.txt" }, "Mod/Readme.txt")]
    [InlineData(new[] { "Mod/A Readme.txt", "Mod/README.md" }, "Mod/README.md")]
    [InlineData(new[] { "Mod/Changes.txt", "Mod/Plugin.esp" }, null)]
    public async Task TheReadmeIsTheTextFileThatSaysSoNearestTheTop(string[] paths, string? readme)
    {
        _reader.Entries[ArchivePath] = [.. paths.Select(path => new ArchiveEntry(path, 10, IsFolder: false))];

        Assert.Equal(readme, (await _contents.ListAsync(Archive, Cancellation)).Readme);
    }

    [Fact]
    public async Task RefusesAFileTheLibraryDoesntHaveOneNotOnDiskAndOneThatIsntAnArchive()
    {
        _storage.Remove(ArchivePath);

        KeyNotFoundException unknown = await Assert.ThrowsAsync<KeyNotFoundException>(() => _contents.ListAsync(new ModFileRef(SkyUi, "1"), Cancellation));
        FileNotFoundException missing = await Assert.ThrowsAsync<FileNotFoundException>(() => _contents.ListAsync(Archive, Cancellation));
        ArchiveUnreadableException plugin = await Assert.ThrowsAsync<ArchiveUnreadableException>(() => _contents.ListAsync(Plugin, Cancellation));

        Assert.Equal($"The library doesn't have the file {new ModFileRef(SkyUi, "1")}.", unknown.Message);
        Assert.Equal("SkyUI_5_2_SE.7z isn't in the library's folder any more. A rescan finds it if it was moved.", missing.Message);
        Assert.Equal((ArchiveTrouble.NotAnArchive, "SkyUI.esp isn't an archive ModShelf can look into: it reads .zip, .7z and .rar files."), (plugin.Trouble, plugin.Message));
        Assert.True(_contents.CanList("SkyUI_5_2_SE.7z"));
        Assert.False(_contents.CanList("SkyUI.esp"));
    }

    [Fact]
    public async Task SaysInTheLogWhyAnArchiveCantBeReadAndDoesntTryAgain()
    {
        _reader.Failure = new ArchiveUnreadableException(ArchiveTrouble.Encrypted, "SkyUI_5_2_SE.7z needs a password, which ModShelf doesn't ask for.");

        ArchiveUnreadableException listed = await Assert.ThrowsAsync<ArchiveUnreadableException>(() => _contents.ListAsync(Archive, Cancellation));
        ArchiveUnreadableException read = await Assert.ThrowsAsync<ArchiveUnreadableException>(() => _contents.ReadTextAsync(Archive, "SkyUI/Readme.txt", Cancellation));

        Assert.Same(_reader.Failure, listed);
        Assert.Same(_reader.Failure, read);
        Assert.Single(_reader.Read);
        Assert.Equal(2, _logger.Entries.Count(entry => entry.Contains("SkyUI_5_2_SE.7z couldn't be read: Encrypted", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(new byte[] { 0x48, 0xC3, 0xA9 }, "Hé")]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x48, 0x69 }, "Hi")]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x48, 0x00, 0xE9, 0x00 }, "Hé")]
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x48, 0x00, 0xE9 }, "Hé")]
    [InlineData(new byte[] { 0x48, 0xE9, 0x21 }, "Hé!")]
    [InlineData(new byte[] { }, "")]
    public async Task ReadsTextAsUtf8OrUtf16ByItsMarkOrElseLatin1(byte[] bytes, string text)
    {
        _reader.Files[(ArchivePath, "SkyUI/Readme.txt")] = bytes;

        ArchiveText read = await _contents.ReadTextAsync(Archive, "./SkyUI\\Readme.txt", Cancellation);

        Assert.Equal(new ArchiveText("SkyUI/Readme.txt", text, bytes.Length, IsCut: false), read);
    }

    [Fact]
    public async Task ReadsTheStartOfALargeFileWithoutACharacterTheEndCuts()
    {
        // "é" is two bytes: the first one is the last byte read.
        byte[] bytes = Encoding.UTF8.GetBytes(new string('a', ILibraryContents.MaxTextBytes - 1) + "é and more");
        _reader.Files[(ArchivePath, "SkyUI/Big.txt")] = bytes;

        ArchiveText read = await _contents.ReadTextAsync(Archive, "SkyUI/Big.txt", Cancellation);

        Assert.True(read.IsCut);
        Assert.Equal(bytes.Length, read.SizeInBytes);
        Assert.Equal(new string('a', ILibraryContents.MaxTextBytes - 1), read.Text);
    }

    [Fact]
    public async Task RefusesAFileThatIsntText()
    {
        _reader.Files[(ArchivePath, "SkyUI/Readme.txt")] = [0x4D, 0x5A, 0x90, 0x00, 0x03];

        ArchiveUnreadableException refused = await Assert.ThrowsAsync<ArchiveUnreadableException>(() => _contents.ReadTextAsync(Archive, "SkyUI/Readme.txt", Cancellation));

        Assert.Equal((ArchiveTrouble.NotText, "SkyUI/Readme.txt in SkyUI_5_2_SE.7z isn't text."), (refused.Trouble, refused.Message));
    }

    [Fact]
    public async Task FindsTheLibrarysModByItsPageWithoutAskingItsSite()
    {
        LibraryMod found = await _contents.FindModAsync(new Uri("https://nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35408"), Cancellation);
        KeyNotFoundException unknown = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _contents.FindModAsync(new Uri("https://www.nexusmods.com/skyrimspecialedition/mods/3863"), Cancellation));

        Assert.Equal(SkyUi, found.Mod);
        Assert.Equal("The library has no mod at https://www.nexusmods.com/skyrimspecialedition/mods/3863.", unknown.Message);
    }

    private static LibraryFile File(ModFileRef file, string relativePath)
        => new(file, file.Id, null, ModFileCategory.Main, TestData.Now, relativePath, 100, TestData.Now);
}
