using ModShelf.Application.Lists;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Lists as the test sets them: what an export finds, what a file reads as, and what an import does, recording each.
/// </summary>
internal sealed class ScriptedModLists : IModLists
{
    /// <summary>
    /// What each export was asked for.
    /// </summary>
    public List<ListScope> Exported { get; } = [];

    /// <summary>
    /// The list an export finds.
    /// </summary>
    public ModList Export { get; set; } = new(null, TestData.Now, []);

    /// <summary>
    /// The text each file read held.
    /// </summary>
    public List<string> ReadTexts { get; } = [];

    /// <summary>
    /// The list a file reads as.
    /// </summary>
    public ModList ReadList { get; set; } = new(null, TestData.Now, []);

    /// <summary>
    /// Why a file isn't a list, when it isn't.
    /// </summary>
    public string? Unreadable { get; set; }

    /// <summary>
    /// The collections, as lists, by their links; any other link isn't one.
    /// </summary>
    public Dictionary<Uri, ModList> Collections { get; } = [];

    /// <summary>
    /// What reading any collection fails with, when set.
    /// </summary>
    public Exception? CollectionRefusal { get; set; }

    /// <summary>
    /// Each link read as a collection.
    /// </summary>
    public List<Uri> CollectionsAsked { get; } = [];

    /// <summary>
    /// Each import, with its choices.
    /// </summary>
    public List<(ListImportPlan Plan, ListImportOptions Options)> Imports { get; } = [];

    /// <summary>
    /// What an import does; by default, nothing.
    /// </summary>
    public Func<ListImportPlan, ListImportOptions, IProgress<int>?, CancellationToken, Task<ListImportResult>> Importing { get; set; }
        = (_, _, _, _) => Task.FromResult(new ListImportResult(0, [], 0, []));

    /// <summary>
    /// The plan a list makes: by default, every mod new, with none of its files in the library.
    /// </summary>
    public static ListImportPlan PlanOf(ModList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return new ListImportPlan(
            list,
            [.. list.Mods.Select(mod => new PlannedMod(mod, PlannedModState.New, [.. mod.Files.Select(file => new PlannedFile(file, false))]))]);
    }

    public Task<ModList> ExportAsync(ListScope scope, CancellationToken cancellationToken)
    {
        Exported.Add(scope);
        return Task.FromResult(Export);
    }

    public string Write(ModList list) => $"A list of {list.Mods.Count} mods";

    public ModList Read(string text)
    {
        ReadTexts.Add(text);
        return Unreadable is { } why ? throw new InvalidDataException(why) : ReadList;
    }

    public Task<ModList?> ReadCollectionAsync(Uri link, CancellationToken cancellationToken)
    {
        CollectionsAsked.Add(link);
        return CollectionRefusal is { } refusal
            ? Task.FromException<ModList?>(refusal)
            : Task.FromResult(Collections.GetValueOrDefault(link));
    }

    public Task<ListImportPlan> PlanAsync(ModList list, CancellationToken cancellationToken) => Task.FromResult(PlanOf(list));

    public Task<ListImportResult> ImportAsync(
        ListImportPlan plan,
        ListImportOptions options,
        IProgress<int>? progress,
        Func<Uri, CancellationToken, Task<bool>>? queue,
        CancellationToken cancellationToken)
    {
        Imports.Add((plan, options));
        return Importing(plan, options, progress, cancellationToken);
    }
}
