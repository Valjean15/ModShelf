using System.Collections.Concurrent;

using ModShelf.Application.Endorsements;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Endorsements;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// The accounts' endorsements as the test sets them: the sites that endorse, what each account said, and a refusal for
/// each mod the test names. It records each endorsement given, and each list read.
/// </summary>
internal sealed class ScriptedEndorsements : IEndorsements
{
    private readonly ConcurrentQueue<(ModRef Mod, Endorsement Endorsement, string? Version)> _given = new();
    private readonly ConcurrentQueue<SourceId> _read = new();

    /// <summary>
    /// The sites that let ModShelf endorse; none unless the test adds them.
    /// </summary>
    public HashSet<SourceId> Sources { get; } = [];

    /// <summary>
    /// What the accounts said, by mod.
    /// </summary>
    public ConcurrentDictionary<ModRef, Endorsement> Said { get; } = new();

    /// <summary>
    /// What reading a site's list throws instead, when set.
    /// </summary>
    public Exception? ListFailure { get; set; }

    /// <summary>
    /// What endorsing each mod throws instead, by the mod.
    /// </summary>
    public ConcurrentDictionary<ModRef, Exception> Refusals { get; } = new();

    public IReadOnlyList<(ModRef Mod, Endorsement Endorsement, string? Version)> Given => [.. _given];

    public IReadOnlyList<SourceId> Read => [.. _read];

    public bool CanEndorse(SourceId source) => Sources.Contains(source);

    public Task<IReadOnlyDictionary<ModRef, Endorsement>> ListAsync(SourceId source, CancellationToken cancellationToken)
    {
        _read.Enqueue(source);
        return ListFailure is { } failure
            ? Task.FromException<IReadOnlyDictionary<ModRef, Endorsement>>(failure)
            : Task.FromResult<IReadOnlyDictionary<ModRef, Endorsement>>(Said.Where(said => said.Key.Game.Source == source).ToDictionary());
    }

    public Task EndorseAsync(ModRef modRef, Endorsement endorsement, string? version, CancellationToken cancellationToken)
    {
        if (Refusals.TryGetValue(modRef, out Exception? refusal))
        {
            return Task.FromException(refusal);
        }

        _given.Enqueue((modRef, endorsement, version));
        Said[modRef] = endorsement;
        return Task.CompletedTask;
    }

    public Task<ModRef> EndorseAsync(Uri link, Endorsement endorsement, CancellationToken cancellationToken)
        => throw new NotSupportedException("The screens endorse by reference.");
}
