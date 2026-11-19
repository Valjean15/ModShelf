using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Tests.Common;

/// <summary>
/// A library with something of each kind to free up space from, against the fake catalog site, whose mods list files 11,
/// 12, 20 and 50, an old version, 30, and an archived one, 40. It has Example Mod (1), watched, with 12 and, in its
/// <c>_archive</c>, 30 and 40, beside a readme that matches nothing; and Other Mod (5), no longer watched, with 11, and
/// 20, a copy of Example Mod's 12. Each file is a few bytes.
/// </summary>
internal static class ClutteredLibrary
{
    /// <summary>
    /// Puts the two mods in the library, with their files on disk.
    /// </summary>
    /// <returns>The library's folder.</returns>
    public static Task<string> StoreAsync(InProcessCli cli, CancellationToken cancellationToken) => cli.InAppAsync(
        async services =>
        {
            string root = await services.GetRequiredService<ISettingsService>().GetLibraryRootAsync(cancellationToken);
            ILibraryRepository repository = services.GetRequiredService<ILibraryRepository>();
            ModRef example = new(new GameRef(FakeCatalogSite.SourceId, "game"), "1");
            ModRef other = new(new GameRef(FakeCatalogSite.SourceId, "game"), "5");
            LibraryMod stored = LibraryMod.Watch(example, "Example Mod", "Someone", FakeCatalogSite.ModLink("1"), DateTimeOffset.UnixEpoch)
                .WithFile(await PlaceAsync(root, example, "12", "fake-catalog/game/1/file-12.zip", "twelve!", cancellationToken))
                .WithFile(await PlaceAsync(root, example, "30", "fake-catalog/game/1/_archive/file-30.zip", "thirty", cancellationToken))
                .WithFile(await PlaceAsync(root, example, "40", "fake-catalog/game/1/_archive/file-40.zip", "forty", cancellationToken));
            await repository.UpdateAsync(example, _ => stored, cancellationToken);
            LibraryMod unwatched = LibraryMod.Watch(other, "Other Mod", "Someone", FakeCatalogSite.ModLink("5"), DateTimeOffset.UnixEpoch)
                .WithFile(await PlaceAsync(root, other, "11", "fake-catalog/game/5/file-11.zip", "eleven", cancellationToken))
                .WithFile(await PlaceAsync(root, other, "20", "fake-catalog/game/5/file-20.zip", "twelve!", cancellationToken))
                .Unwatched()!;
            await repository.UpdateAsync(other, _ => unwatched, cancellationToken);
            await File.WriteAllTextAsync(LibraryPaths.Resolve(root, "fake-catalog/game/1/Readme.txt"), "read me!", cancellationToken);
            return root;
        },
        cancellationToken);

    private static async Task<LibraryFile> PlaceAsync(string root, ModRef mod, string id, string relativePath, string content, CancellationToken cancellationToken)
    {
        string path = LibraryPaths.Resolve(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new LibraryFile(
            new ModFileRef(mod, id), $"File {id}", null, ModFileCategory.Main, DateTimeOffset.UnixEpoch, relativePath, content.Length, DateTimeOffset.UnixEpoch, sha256);
    }
}
