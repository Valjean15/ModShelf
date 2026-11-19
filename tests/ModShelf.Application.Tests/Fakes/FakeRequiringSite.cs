using System.Collections.Concurrent;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// The <see cref="FakeModSite"/>'s catalog, whose mods need what the test says: other mods of the site, by their IDs, or
/// things off it. It records each mod whose requirements it's asked for, and can refuse some.
/// </summary>
internal sealed class FakeRequiringSite(FakeModSite site) : IModCatalog, IModRequirements
{
    private readonly ConcurrentQueue<string> _asked = new();

    public SourceId Id => FakeModSite.SourceId;

    public string DisplayName => site.DisplayName;

    public SiteWords Words => SiteWords.None;

    /// <summary>
    /// What each mod needs, by its ID.
    /// </summary>
    public ConcurrentDictionary<string, ModRequirement[]> Needs { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// What reading a mod's requirements throws instead, by its ID.
    /// </summary>
    public ConcurrentDictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The mods whose requirements were read, by their IDs, in order.
    /// </summary>
    public IReadOnlyList<string> Asked => [.. _asked];

    /// <summary>
    /// A mod of the site the test's mod needs.
    /// </summary>
    public static ModRequirement Mod(string modId, string? notes = null) => new($"Mod {modId}", FakeModSite.Mod(modId), notes: notes);

    public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken) => site.GetGamesAsync(cancellationToken);

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken) => site.GetGameAsync(game, cancellationToken);

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken) => site.GetModAsync(modRef, cancellationToken);

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken) => site.GetFilesAsync(modRef, cancellationToken);

    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
        => site.GetChangelogsAsync(modRef, cancellationToken);

    public Task<IReadOnlyList<ModRequirement>> GetRequirementsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        if (modRef.Game.Source != Id)
        {
            throw new ArgumentException("That's another site's mod.", nameof(modRef));
        }

        _asked.Enqueue(modRef.Id);
        return Failures.TryGetValue(modRef.Id, out Exception? failure)
            ? Task.FromException<IReadOnlyList<ModRequirement>>(failure)
            : Task.FromResult<IReadOnlyList<ModRequirement>>(Needs.GetValueOrDefault(modRef.Id) ?? []);
    }
}
