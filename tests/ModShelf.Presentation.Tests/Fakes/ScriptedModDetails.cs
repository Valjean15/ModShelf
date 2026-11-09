using System.Collections.Concurrent;

using ModShelf.Application.Catalog;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Mods' details as the test sets them: the saved ones, and the site's, which the saved ones stand in for until set. It
/// records what it's asked.
/// </summary>
internal sealed class ScriptedModDetails : IModDetails
{
    private readonly ConcurrentQueue<string> _asked = new();
    private readonly ConcurrentQueue<int> _askedAtOnce = new();

    public ConcurrentDictionary<ModRef, ModInfo> Saved { get; } = new();

    public ConcurrentDictionary<ModRef, ModInfo> Site { get; } = new();

    /// <summary>
    /// Holds the site's answers until set; null to answer at once.
    /// </summary>
    public TaskCompletionSource? SiteHeld { get; set; }

    /// <summary>
    /// What was asked, such as "saved 12604" or "site 12604", oldest first.
    /// </summary>
    public IReadOnlyList<string> Asked => [.. _asked];

    /// <summary>
    /// How many mods each ask for several mods' saved details held, oldest first; each mod is in <see cref="Asked"/> too.
    /// </summary>
    public IReadOnlyList<int> AskedAtOnce => [.. _askedAtOnce];

    public Task<ModInfo?> GetSavedAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"saved {modRef.Id}");
        return Task.FromResult(Saved.GetValueOrDefault(modRef));
    }

    public Task<IReadOnlyDictionary<ModRef, ModInfo>> GetSavedAsync(IReadOnlyCollection<ModRef> mods, CancellationToken cancellationToken)
    {
        _askedAtOnce.Enqueue(mods.Count);
        foreach (ModRef mod in mods)
        {
            _asked.Enqueue($"saved {mod.Id}");
        }

        return Task.FromResult<IReadOnlyDictionary<ModRef, ModInfo>>(mods.Where(Saved.ContainsKey).Distinct().ToDictionary(mod => mod, mod => Saved[mod]));
    }

    public async Task<ModInfo?> GetAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        _asked.Enqueue($"site {modRef.Id}");
        if (SiteHeld is { } held)
        {
            await held.Task.WaitAsync(cancellationToken);
        }

        return Site.GetValueOrDefault(modRef) ?? Saved.GetValueOrDefault(modRef);
    }
}
