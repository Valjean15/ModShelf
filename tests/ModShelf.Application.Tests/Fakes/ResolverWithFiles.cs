using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A site that resolves downloads as <paramref name="resolver"/> does, and lists files for every mod: those the test
/// gives, such as with the hashes a downloaded file is checked against. Asking for the files fails when
/// <see cref="Unreachable"/> says so.
/// </summary>
internal sealed class ResolverWithFiles(ScriptedResolver resolver) : IDownloadResolver, IModCatalog
{
    public SourceId Id => resolver.Id;

    public string DisplayName => resolver.DisplayName;

    public SiteWords Words => SiteWords.None;

    public bool HandsOverLinks => resolver.HandsOverLinks;

    public List<ModFile> Files { get; } = [];

    /// <summary>
    /// Whether the site can't be asked for a mod's files.
    /// </summary>
    public bool Unreachable { get; set; }

    public Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
        => resolver.ResolveAsync(file, handoff, cancellationToken);

    public Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
        => Unreachable
            ? Task.FromException<IReadOnlyList<ModFile>>(new HttpRequestException("The site can't be reached."))
            : Task.FromResult<IReadOnlyList<ModFile>>([.. Files.Where(file => file.Reference.Mod == modRef)]);

    public Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();
}
