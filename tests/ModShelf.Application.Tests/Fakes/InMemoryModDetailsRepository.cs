using System.Collections.Concurrent;

using ModShelf.Application.Catalog.Ports;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps mods' details in memory. The test can make finding them fail.
/// </summary>
internal sealed class InMemoryModDetailsRepository : IModDetailsRepository
{
    private readonly ConcurrentDictionary<ModRef, SavedModDetails> _saved = new();

    /// <summary>
    /// What finding throws, while set.
    /// </summary>
    public Exception? FindFailure { get; set; }

    public int Saves { get; private set; }

    /// <summary>
    /// How many times the details of several mods were asked for at once.
    /// </summary>
    public int Finds { get; private set; }

    public SavedModDetails? this[ModRef mod] => _saved.GetValueOrDefault(mod);

    public void Add(SavedModDetails details) => _saved[details.Details.Mod] = details;

    public Task<SavedModDetails?> FindAsync(ModRef modRef, CancellationToken cancellationToken)
        => FindFailure is { } failure ? Task.FromException<SavedModDetails?>(failure) : Task.FromResult(this[modRef]);

    public Task<IReadOnlyDictionary<ModRef, SavedModDetails>> FindAsync(IReadOnlyCollection<ModRef> mods, CancellationToken cancellationToken)
    {
        Finds++;
        return FindFailure is { } failure
            ? Task.FromException<IReadOnlyDictionary<ModRef, SavedModDetails>>(failure)
            : Task.FromResult<IReadOnlyDictionary<ModRef, SavedModDetails>>(mods.Where(_saved.ContainsKey).Distinct().ToDictionary(mod => mod, mod => _saved[mod]));
    }

    public Task SaveAsync(SavedModDetails details, CancellationToken cancellationToken)
    {
        Add(details);
        Saves++;
        return Task.CompletedTask;
    }
}
