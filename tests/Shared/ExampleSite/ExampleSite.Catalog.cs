using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Testing;

public sealed partial class ExampleSite
{
    public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<GameInfo>>([StardewValley, PixelFarm]);

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        EnsureOwn(game, nameof(game));
        return Task.FromResult(
            game == StardewValley.Game ? StardewValley
            : game == PixelFarm.Game ? PixelFarm
            : throw NotFound($"game {game.Key}"));
    }

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        EnsureOwn(modRef.Game, nameof(modRef));
        return Task.FromResult(Describe(ModNamed(modRef)));
    }

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        EnsureOwn(modRef.Game, nameof(modRef));
        ExampleMod mod = ModNamed(modRef);
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ModFile>>([.. mod.Files]);
        }
    }

    /// <summary>
    /// None: the site keeps no changelogs.
    /// </summary>
    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        EnsureOwn(modRef.Game, nameof(modRef));
        _ = ModNamed(modRef);
        return Task.FromResult<IReadOnlyList<ModChangelog>>([]);
    }

    /// <summary>
    /// The mod's newest version: the main file uploaded last.
    /// </summary>
    private ModFile NewestMainFile(ExampleMod mod)
    {
        lock (_gate)
        {
            return mod.Files.Where(file => file.Category == ModFileCategory.Main).MaxBy(file => file.UploadedAt)
                ?? throw NotFound($"file of mod {mod.Mod.Id}");
        }
    }

    /// <summary>
    /// The mod as the site describes it: its newest main file gives its version and size, and no approval count, category
    /// or picture.
    /// </summary>
    private ModInfo Describe(ExampleMod mod)
    {
        ModFile[] files;
        lock (_gate)
        {
            files = [.. mod.Files];
        }

        ModFile newest = NewestMainFile(mod);
        return new ModInfo(
            mod.Mod,
            mod.Name,
            mod.Summary,
            mod.Author,
            newest.VersionLabel,
            ModLink(mod.Mod),
            updatedAt: files.Max(file => file.UploadedAt),
            downloads: mod.Downloads,
            size: newest.SizeInBytes,
            publishedAt: files.Min(file => file.UploadedAt));
    }
}
