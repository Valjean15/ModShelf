using System.Collections.Concurrent;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Lookup;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// The <see cref="FakeModSite"/>'s catalog, with a search of its files by MD5 that the test fills in. It records each
/// search, and can refuse them.
/// </summary>
internal sealed class FakeLookupSite(FakeModSite site) : IModCatalog, IFileLookup
{
    private readonly ConcurrentQueue<string> _searched = new();

    public SourceId Id => FakeModSite.SourceId;

    public string DisplayName => site.DisplayName;

    public SiteWords Words => SiteWords.None;

    public FileHashAlgorithm LookupAlgorithm { get; set; } = FileHashAlgorithm.Md5;

    /// <summary>
    /// The files each MD5 finds, as mod and file IDs of the site's.
    /// </summary>
    public ConcurrentDictionary<string, (string Mod, string File)[]> ByMd5 { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What every search throws instead, while set.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// The MD5s searched for, in order.
    /// </summary>
    public IReadOnlyList<string> Searched => [.. _searched];

    public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken) => site.GetGamesAsync(cancellationToken);

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken) => site.GetGameAsync(game, cancellationToken);

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken) => site.GetModAsync(modRef, cancellationToken);

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken) => site.GetFilesAsync(modRef, cancellationToken);

    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
        => site.GetChangelogsAsync(modRef, cancellationToken);

    public Task<IReadOnlyList<FoundFile>> FindByHashAsync(GameRef game, FileHash hash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(hash);
        if (game.Source != Id || hash.Algorithm != LookupAlgorithm)
        {
            throw new ArgumentException("Not this site's game, or not its hash.", nameof(game));
        }

        _searched.Enqueue(hash.Value);
        if (Refusal is { } refusal)
        {
            return Task.FromException<IReadOnlyList<FoundFile>>(refusal);
        }

        return Task.FromResult<IReadOnlyList<FoundFile>>(
        [
            .. (ByMd5.GetValueOrDefault(hash.Value) ?? []).Select(found => new FoundFile(
                new ModInfo(FakeModSite.Mod(found.Mod), $"Mod {found.Mod}", author: "Someone", pageUrl: new Uri($"https://mods.example/{found.Mod}")),
                site.FileOf(found.Mod, found.File))),
        ]);
    }
}
