using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.QuickSearch;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.QuickSearch;

/// <summary>
/// Quick search with a site that finds its games by name, as GameBanana does: its games found as the name is typed, the
/// library's named by it.
/// </summary>
public sealed class QuickSearchGamesByNameTests
{
    private static readonly LibraryMod Protagonist = TestData.LibraryMod(new ModRef(QuickSearchHarness.Royal, "440088"), "P5R Female Protagonist");

    private readonly QuickSearchHarness _harness = new(banana: true);

    private QuickSearchViewModel Search => _harness.Search;

    [Fact]
    public async Task FindsTheSitesGamesAsTheNameIsTypedTheLibrarysByTheirNames()
    {
        await _harness.OpenAsync(Protagonist);

        Search.Query = "persona";
        await Search.Finding;

        Assert.Equal(
            [
                "# Games",
                "> Persona 5 Royal (PC) — In your library · 1 mod",
                "Persona 5 — On GameBanana",
                "# Settings",
                "Games › Persona 5 Royal (PC)",
            ],
            _harness.Lines());
        Assert.DoesNotContain($"GetGamesAsync {QuickSearchHarness.Banana}", _harness.Lookup.Asked);
        Assert.Null(Search.FindingText);
    }

    [Fact]
    public async Task SaysItsFindingUntilTheSiteAnswers()
    {
        await _harness.OpenAsync();
        TaskCompletionSource answer = new();
        _harness.Lookup.HeldFinds.Enqueue(answer);

        Search.Query = "qqzx";
        string? finding = Search.FindingText;
        string? nothingWhileFinding = Search.NothingFound;
        answer.SetResult();
        await Search.Finding;

        Assert.Equal("Finding games on GameBanana…", finding);
        Assert.Null(nothingWhileFinding);
        Assert.Equal("Nothing found for “qqzx”.", Search.NothingFound);
    }
}
