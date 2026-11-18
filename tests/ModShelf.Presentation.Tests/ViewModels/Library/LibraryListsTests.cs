using System.Net;

using ModShelf.Application.Lists;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels.Dialogs;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// Export list… and Import list… in the Library: the library, the game open or its selected mods written to the file the
/// user chooses; a list file read and shown in the dialog that imports it, or why it can't be.
/// </summary>
public sealed class LibraryListsTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("3863");

    private readonly LibraryHarness _harness = new();
    private readonly TempFolder _folder = new();
    private readonly CultureScope _culture = new("en-US");

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose()
    {
        _harness.Dispose();
        _folder.Dispose();
        _culture.Dispose();
    }

    [Theory]
    [InlineData(null, "library.modshelf-list.json")]
    [InlineData("skyrimspecialedition", "skyrimspecialedition.modshelf-list.json")]
    [InlineData("Baldur's Gate 3", "Baldur-s-Gate-3.modshelf-list.json")]
    [InlineData("éé", "game.modshelf-list.json")]
    public void AListFilesNameIsItsGamesKeyOrTheLibrarys(string? key, string expected)
        => Assert.Equal(expected, LibraryViewModel.ListFileName(key is null ? null : new GameRef(TestData.Nexus, key)));

    [Fact]
    public async Task ExportsTheWholeLibraryFromItsGamesAndSaysWhere()
    {
        await StartAsync();
        string path = Path.Combine(_folder.Path, "mine.json");
        _harness.Dialogs.SaveFiles.Enqueue(path);
        _harness.Lists.Export = Listing(2);

        await Screen.ExportListCommand.ExecuteAsync(null);

        Assert.Equal([("Export the library as a list", "library.modshelf-list.json")], _harness.Dialogs.SavePickers);
        Assert.Equal([ListScope.Library], _harness.Lists.Exported);
        Assert.Equal("A list of 2 mods", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(
            [("List exported", $"Exported 2 mods and their files to {path}. Import list… brings them into a library, here or on another computer.")],
            _harness.Dialogs.Messages);
    }

    [Fact]
    public async Task ExportsTheGameOpenOrTheModsSelectedInIt()
    {
        await StartAsync();
        _ = Row(SkyUi);
        string gameName = _harness.Game(SkyUi.Game).Name;
        _harness.Dialogs.SaveFiles.Enqueue(Path.Combine(_folder.Path, "game.json"));
        _harness.Dialogs.SaveFiles.Enqueue(Path.Combine(_folder.Path, "selected.json"));
        _harness.Lists.Export = Listing(1);

        await Screen.ExportListCommand.ExecuteAsync(null);
        Row(SkyUi).IsSelected = true;
        await Screen.ExportListCommand.ExecuteAsync(null);

        Assert.Equal(
            [($"Export {gameName}'s mods as a list", "skyrimspecialedition.modshelf-list.json"), ("Export the selected mods as a list", "skyrimspecialedition.modshelf-list.json")],
            _harness.Dialogs.SavePickers);
        Assert.Equal(
            [(SkyUi.Game, (ModRef[]?)null), (SkyUi.Game, [SkyUi])],
            _harness.Lists.Exported.Select(scope => (scope.Game, scope.Mods?.ToArray())));
        Assert.StartsWith($"Exported 1 mod and its files to {Path.Combine(_folder.Path, "selected.json")}.", _harness.Dialogs.Messages[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellingWhereToSaveExportsNothing()
    {
        await StartAsync();

        await Screen.ExportListCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Lists.Exported);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Fact]
    public async Task AFileThatCantBeWrittenIsSaid()
    {
        await StartAsync();
        _harness.Dialogs.SaveFiles.Enqueue(Path.Combine(_folder.Path, "missing", "list.json"));

        await Screen.ExportListCommand.ExecuteAsync(null);

        Assert.Equal("The list wasn't exported", Assert.Single(_harness.Dialogs.Messages).Title);
    }

    [Fact]
    public async Task ImportReadsTheFileChosenAndShowsWhatImportingWouldDo()
    {
        await StartAsync();
        string path = Path.Combine(_folder.Path, "list.json");
        await File.WriteAllTextAsync(path, "the list", TestContext.Current.CancellationToken);
        _harness.Dialogs.Files.Enqueue(path);
        _harness.Lists.ReadList = Listing(2) with { Title = "Essentials" };

        await Screen.ImportListCommand.ExecuteAsync(null);

        Assert.Equal([("Choose a list to import", (string?)null)], _harness.Dialogs.FilePickers);
        Assert.Equal(["the list"], _harness.Lists.ReadTexts);
        ImportListViewModel shown = Assert.Single(_harness.Dialogs.ImportingLists);
        Assert.Equal(("Import “Essentials”", "2 in the list: 2 new to the library, 0 in it already"), (shown.Title, shown.Facts[0].Value));
        Assert.Empty(_harness.Lists.Imports);
    }

    [Fact]
    public async Task TheDialogImportsTheList()
    {
        await StartAsync();
        string path = Path.Combine(_folder.Path, "list.json");
        await File.WriteAllTextAsync(path, "the list", TestContext.Current.CancellationToken);
        _harness.Dialogs.Files.Enqueue(path);
        _harness.Lists.ReadList = Listing(1);
        _harness.Dialogs.InImportList = importing => importing.ImportCommand.ExecuteAsync(null);

        await Screen.ImportListCommand.ExecuteAsync(null);

        (ListImportPlan plan, ListImportOptions options) = Assert.Single(_harness.Lists.Imports);
        Assert.Same(_harness.Lists.ReadList, plan.List);
        Assert.Equal(new ListImportOptions(), options);
    }

    [Fact]
    public async Task AFileThatIsntAListSaysWhyAndShowsNoDialog()
    {
        await StartAsync();
        string path = Path.Combine(_folder.Path, "list.json");
        await File.WriteAllTextAsync(path, "{}", TestContext.Current.CancellationToken);
        _harness.Dialogs.Files.Enqueue(path);
        _harness.Lists.Unreadable = "This isn't a ModShelf list: it says no format.";

        await Screen.ImportListCommand.ExecuteAsync(null);

        Assert.Equal([("That isn't a list ModShelf can import", "This isn't a ModShelf list: it says no format.")], _harness.Dialogs.Messages);
        Assert.Empty(_harness.Dialogs.ImportingLists);
    }

    [Fact]
    public async Task AFileFarTooLargeIsntRead()
    {
        await StartAsync();
        string path = Path.Combine(_folder.Path, "huge.json");
        await using (FileStream huge = File.Create(path))
        {
            huge.SetLength((64L * 1024 * 1024) + 1);
        }

        _harness.Dialogs.Files.Enqueue(path);

        await Screen.ImportListCommand.ExecuteAsync(null);

        Assert.Equal([("That isn't a list ModShelf can import", "The file is too large to be a ModShelf list.")], _harness.Dialogs.Messages);
        Assert.Empty(_harness.Lists.ReadTexts);
    }

    [Fact]
    public async Task CancellingTheFileImportsNothing()
    {
        await StartAsync();

        await Screen.ImportListCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Lists.ReadTexts);
        Assert.Empty(_harness.Dialogs.ImportingLists);
    }

    [Fact]
    public async Task ImportsACollectionByItsLinkThroughTheSameDialog()
    {
        Uri link = new("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx");
        _harness.Lists.Collections[link] = Listing(1) with { Title = "Essentials" };
        _harness.Dialogs.Answers.Enqueue($"  {link.AbsoluteUri} ");

        await Screen.ImportCollectionCommand.ExecuteAsync(null);

        TextQuestion asked = Assert.Single(_harness.Dialogs.Questions);
        Assert.Equal(("Import a collection", "Read collection"), (asked.Title, asked.ConfirmLabel));
        Assert.Equal([link], _harness.Lists.CollectionsAsked);
        Assert.Equal("Import “Essentials”", Assert.Single(_harness.Dialogs.ImportingLists).Title);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Theory]
    [InlineData("not a link", null, "That isn't a complete link. Copy the address of the collection's page.")]
    [InlineData("https://example.com/lists/mine", null, "That link isn't to a collection ModShelf can read, such as a Nexus Mods collection's page.")]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx", "gone", "The site has no such collection.")]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx", "account", "The site needs a linked account to read the collection, or refused the one linked. Link it in Settings › Accounts, then try again.")]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx", "down", "The site couldn't be asked: Service unavailable.")]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/q-x", "malformed", "That Nexus Mods collection link doesn't name a collection.")]
    public async Task SaysWhyACollectionCantBeRead(string answer, string? refusal, string said)
    {
        _harness.Lists.CollectionRefusal = refusal switch
        {
            "gone" => new HttpRequestException("Not found.", null, HttpStatusCode.NotFound),
            "account" => new AuthenticationFailedException("No account."),
            "down" => new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable),
            "malformed" => new ArgumentException("That Nexus Mods collection link doesn't name a collection.", nameof(answer)),
            _ => null,
        };
        _harness.Dialogs.Answers.Enqueue(answer);

        await Screen.ImportCollectionCommand.ExecuteAsync(null);

        Assert.Equal([("The collection couldn't be read", said)], _harness.Dialogs.Messages);
        Assert.Empty(_harness.Dialogs.ImportingLists);
    }

    [Fact]
    public async Task CancellingTheLinkReadsNothing()
    {
        await Screen.ImportCollectionCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Lists.CollectionsAsked);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    private static ModList Listing(int mods)
        => new(
            null,
            TestData.Now,
            [.. new[] { SkyUi, Address }.Take(mods).Select(mod => new ListedMod(
                mod,
                $"Mod {mod.Id}",
                null,
                null,
                Watched: true,
                Pinned: false,
                Note: null,
                [new ListedFile(new ModFileRef(mod, "1"), "Main", "1.0", ModFileCategory.Main, Optional: false)]))]);

    private LibraryModViewModel Row(ModRef mod) => _harness.Row(TestData.LibraryMod(mod, "any"));

    private Task StartAsync()
        => _harness.StartAsync(
            TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "SkyUI.7z")),
            TestData.LibraryMod(Address, "Address Library", watched: true, TestData.LibraryFile(Address, "1", "Address.7z")));
}
