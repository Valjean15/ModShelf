using ModShelf.Application.Archives;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// An archive import that answers as the test says: a scan of the archives it lists, a plan of them all identified
/// unless the test gives one, and what importing does. It can hold identifying and importing back until released.
/// </summary>
internal sealed class ScriptedArchiveImport : IArchiveImport
{
    /// <summary>
    /// The archives a scan finds, by name; each scan is of the folder and game asked.
    /// </summary>
    public List<string> Names { get; } = ["SkyUI.7z", "SKSE.7z"];

    /// <summary>
    /// The requests a scan says identifying takes.
    /// </summary>
    public int Requests { get; set; } = 3;

    public bool CanLookUp { get; set; } = true;

    /// <summary>
    /// What scanning throws instead, while set.
    /// </summary>
    public Exception? ScanFailure { get; set; }

    /// <summary>
    /// What identifying gives, from the scan; every archive identified unless set.
    /// </summary>
    public Func<ArchiveScan, ArchivePlan>? Identified { get; set; }

    /// <summary>
    /// What importing gives, from the plan; every archive to import imported unless set.
    /// </summary>
    public Func<ArchivePlan, ArchiveImportResult>? Imported { get; set; }

    /// <summary>
    /// Holds identifying, after its first archive, until it's set.
    /// </summary>
    public TaskCompletionSource? HoldIdentifying { get; set; }

    /// <summary>
    /// Holds importing, after its first archive, until it's set.
    /// </summary>
    public TaskCompletionSource? HoldImporting { get; set; }

    public List<(string Folder, GameRef Game)> Scanned { get; } = [];

    public int Identifications { get; private set; }

    public List<ArchivePlan> Imports { get; } = [];

    public Task<ArchiveScan> ScanAsync(string folder, GameRef game, CancellationToken cancellationToken)
    {
        Scanned.Add((folder, game));
        return ScanFailure is { } failure
            ? Task.FromException<ArchiveScan>(failure)
            : Task.FromResult(new ArchiveScan(
                folder,
                game,
                [.. Names.Select(name => new ScannedArchive(Path.Combine(folder, name), 100, null, null, null))],
                0,
                Requests,
                CanLookUp));
    }

    public async Task<ArchivePlan> IdentifyAsync(ArchiveScan scan, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        Identifications++;
        progress?.Report(1);
        if (HoldIdentifying is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        progress?.Report(scan.Archives.Count);
        return Identified?.Invoke(scan) ?? new ArchivePlan(
            scan.Folder,
            scan.Game,
            [
                .. scan.Archives.Select((archive, index) => new PlannedArchive(
                    archive.Path, archive.SizeInBytes, ArchiveState.Identified, ArchiveClue.Hash, new ModFileRef(new ModRef(scan.Game, $"{index + 1}"), "1"), $"Mod {index + 1}", "Main file")),
            ]);
    }

    public async Task<ArchiveImportResult> ImportAsync(ArchivePlan plan, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        Imports.Add(plan);
        progress?.Report(1);
        if (HoldImporting is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        return Imported?.Invoke(plan) ?? new ArchiveImportResult(
            [
                .. plan.ToImport.Select(archive => new ImportedArchive(
                    archive.Path,
                    archive.File!.Mod,
                    new LibraryFile(archive.File, archive.FileName!, null, ModFileCategory.Main, DateTimeOffset.UnixEpoch, $"mods/{archive.Name}", 100, DateTimeOffset.UnixEpoch, null))),
            ],
            plan.NewMods,
            0,
            []);
    }
}
