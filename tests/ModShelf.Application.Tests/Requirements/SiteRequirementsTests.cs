using System.Net;

using ModShelf.Application.Downloads;
using ModShelf.Application.Requirements;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Common;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Testing;

using static ModShelf.Application.Tests.Fakes.FakeModSite;

namespace ModShelf.Application.Tests.Requirements;

/// <summary>
/// A mod's requirements, planned and queued: mod 10 needs mod 20, which the library lacks; mod 30, which it has; mod 40,
/// downloading; a script extender off the site; mod 50, with no main file; mod 60, whose files can't be read; and mod 10
/// itself. Deeper, mod 20 needs mod 21 and mod 10, mod 21 needs mod 22 and mod 40, and mod 22 needs mod 23.
/// </summary>
public sealed class SiteRequirementsTests
{
    private static readonly Uri ScriptExtender = new("https://skse.example/");

    private readonly FakeModSite _site = new();
    private readonly FakeRequiringSite _requiring;
    private readonly InMemoryLibraryRepository _library = new();
    private readonly RecordingDownloadService _downloads;
    private readonly RecordingLibraryDownloads _libraryDownloads = new();
    private readonly RecordingLogger<SiteRequirements> _logger = new();

    public SiteRequirementsTests()
    {
        _requiring = new FakeRequiringSite(_site);
        foreach (string mod in new[] { "20", "21", "22", "23", "30", "40" })
        {
            _site.AddMod(mod, $"Mod {mod}");
            _site.AddFile(mod, $"{mod}1", $"old-{mod}.7z", uploadedAt: TestData.Now.AddDays(-30));
            _site.AddFile(mod, $"{mod}2", $"mod-{mod}.7z", uploadedAt: TestData.Now.AddDays(-2));
            _site.AddFile(mod, $"{mod}3", $"extra-{mod}.7z", category: ModFileCategory.Optional, uploadedAt: TestData.Now);
        }

        _site.AddMod("50", "Mod 50");
        _site.AddFile("50", "501", "optional-50.7z", category: ModFileCategory.Optional);
        _site.ModFailures["60"] = new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);
        _requiring.Needs["10"] =
        [
            FakeRequiringSite.Mod("20", notes: "For the main file"),
            FakeRequiringSite.Mod("30"),
            FakeRequiringSite.Mod("40"),
            new ModRequirement("Script Extender", pageUrl: ScriptExtender),
            FakeRequiringSite.Mod("50"),
            FakeRequiringSite.Mod("60"),
            FakeRequiringSite.Mod("10"),
        ];
        _requiring.Needs["20"] = [FakeRequiringSite.Mod("21"), FakeRequiringSite.Mod("10"), new ModRequirement("Script Extender", pageUrl: ScriptExtender)];
        _requiring.Needs["21"] = [FakeRequiringSite.Mod("22"), FakeRequiringSite.Mod("40")];
        _requiring.Needs["22"] = [FakeRequiringSite.Mod("23")];
        _library.Add(LibraryMod.Restore(Mod("30"), "Mod 30", null, null, isWatched: true, TestData.Now, null, [Stored("30", "302")]));
        _downloads = new RecordingDownloadService(
            RecordingDownloadService.Job(File("40", "402"), "library", "mod-40.7z", DownloadJobStatus.Downloading),
            RecordingDownloadService.Job(File("20", "202"), "library", "mod-20.7z", DownloadJobStatus.Failed));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListsWhatTheModNeedsAndWhatDownloadingItWithThemDoes()
    {
        RequirementsPlan plan = await Requirements().PlanAsync(Mod("10"), deeper: false, Cancellation);

        Assert.Equal(
            [
                ("Mod 20", "20", RequirementState.ToDownload, "202", 1),
                ("Mod 30", "30", RequirementState.InLibrary, null, 1),
                ("Mod 40", "40", RequirementState.Downloading, null, 1),
                ("Script Extender", null, RequirementState.Elsewhere, null, 1),
                ("Mod 50", "50", RequirementState.CantDownload, null, 1),
                ("Mod 60", "60", RequirementState.CantDownload, null, 1),
            ],
            plan.Requirements.Select(requirement => (requirement.Name, requirement.Mod?.Id, requirement.State, requirement.File?.Reference.Id, requirement.Depth)));
        Assert.Equal((true, false), (plan.Listed, plan.Deeper));
        Assert.Equal(["Mod 20"], plan.ToDownload.Select(requirement => requirement.Name));
        Assert.Equal("For the main file", plan.Requirements[0].Notes);
        Assert.Equal(ScriptExtender, plan.Requirements[3].PageUrl);
        Assert.Equal(
            [null, null, null, null, "Fake mod site lists no main file of it to download.", "Fake mod site couldn't list its files: Service unavailable."],
            plan.Requirements.Select(requirement => requirement.Why));
        Assert.All(plan.Requirements, requirement => Assert.Equal(Mod("10"), requirement.NeededBy));

        // One request for what it needs, and one for each mod's files the library lacks.
        Assert.Equal(["10"], _requiring.Asked);
        Assert.Equal(["GetFilesAsync 20", "GetFilesAsync 50", "GetFilesAsync 60"], _site.AskedFor);
    }

    [Fact]
    public async Task DeeperGoesDownToTheThirdLevelListingAModOnce()
    {
        RequirementsPlan plan = await Requirements().PlanAsync(Mod("10"), deeper: true, Cancellation);

        // Each mod is followed by what it needs, as in a tree.
        Assert.Equal(
            [("20", 1, "10"), ("21", 2, "20"), ("22", 3, "21"), ("30", 1, "10"), ("40", 1, "10"), (null, 1, "10"), ("50", 1, "10"), ("60", 1, "10")],
            plan.Requirements.Select(requirement => (requirement.Mod?.Id, requirement.Depth, requirement.NeededBy.Id)));
        Assert.Equal(["Mod 20", "Mod 21", "Mod 22"], plan.ToDownload.Select(requirement => requirement.Name));
        Assert.True(plan.Deeper);

        // Only the mods to download are read deeper, and the third level's needs aren't read.
        Assert.Equal(["10", "20", "21"], _requiring.Asked);
    }

    [Fact]
    public async Task ASiteThatListsNoRequirementsPlansNone()
    {
        RequirementsPlan plan = await new SiteRequirements(new SourceRegistry([_site]), _library, _downloads, _libraryDownloads, _logger)
            .PlanAsync(Mod("10"), deeper: true, Cancellation);

        Assert.Equal((false, 0), (plan.Listed, plan.Requirements.Count));
        Assert.Empty(_site.AskedFor);
    }

    [Fact]
    public async Task WhatTheModNeedsMustBeReadButADeeperLevelMayFail()
    {
        _requiring.Failures["21"] = new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable);

        RequirementsPlan deeper = await Requirements().PlanAsync(Mod("10"), deeper: true, Cancellation);
        _requiring.Failures["10"] = new HttpRequestException("Not found.", null, HttpStatusCode.NotFound);
        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(() => Requirements().PlanAsync(Mod("10"), deeper: false, Cancellation));

        Assert.Equal(["20", "21"], deeper.ToDownload.Select(requirement => requirement.Mod!.Id));
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't read the requirements of mod 21 of skyrimspecialedition", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueuesTheChosenRequirementsFilesOneAtATimeOneRefusalNotStoppingTheOthers()
    {
        RequirementsPlan plan = await Requirements().PlanAsync(Mod("10"), deeper: true, Cancellation);
        Uri mod21 = new("https://mods.example/21/212");
        Uri mod22 = new("https://mods.example/22/222");
        _libraryDownloads.InLibrary.Add(mod21);
        _libraryDownloads.Refusals[mod22] = new IOException("Both names are taken.");

        RequirementDownloads queued = await Requirements().QueueAsync([.. plan.Requirements], Cancellation);

        Assert.Equal([new Uri("https://mods.example/20/202"), mod21, mod22], _libraryDownloads.Added);
        Assert.Equal([(false, "20"), (true, "21")], queued.Queued.Select(done => (done.Download.Queued is null, done.Requirement.Mod!.Id)));
        RequirementProblem problem = Assert.Single(queued.Problems);
        Assert.Equal(("22", "Both names are taken."), (problem.Requirement.Mod!.Id, problem.Error.Message));
    }

    [Fact]
    public async Task OnlyTheRequirementsChosenAreQueued()
    {
        RequirementsPlan plan = await Requirements().PlanAsync(Mod("10"), deeper: true, Cancellation);

        RequirementDownloads queued = await Requirements().QueueAsync([plan.ToDownload[1]], Cancellation);

        Assert.Equal([new Uri("https://mods.example/21/212")], _libraryDownloads.Added);
        Assert.Single(queued.Queued);
    }

    private static LibraryFile Stored(string modId, string fileId)
        => new(File(modId, fileId), "Main file", "1.0", ModFileCategory.Main, TestData.Now.AddDays(-2), $"fake-mods/game/Mod {modId} ({modId})/mod-{modId}.7z", 100, TestData.Now.AddDays(-1), null);

    private SiteRequirements Requirements() => new(new SourceRegistry([_requiring]), _library, _downloads, _libraryDownloads, _logger);
}
