using System.IO.Compression;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Tests.Common;

/// <summary>
/// A library whose Example Mod (1), on the fake catalog site, has an archive, file 12, <c>Lanterns.zip</c>, holding a
/// small mod with a readme, and a plugin beside it, file 13, <c>Lanterns.esp</c>, which isn't an archive (ADR 0106).
/// </summary>
internal static class ArchivedLibrary
{
    public const string Readme = "Better Lanterns/Readme.txt";

    public const string ReadmeText = "Better Lanterns 1.2\nPut the .esp in your Data folder.\n";

    /// <summary>
    /// The archive's files and their text, in the order it holds them.
    /// </summary>
    public static readonly (string Path, string Text)[] Files =
    [
        (Readme, ReadmeText),
        ("Better Lanterns/Data/BetterLanterns.esp", "TES4"),
        ("Better Lanterns/Data/Textures/lantern_d.dds", "DDS \0\0\0\0"),
        ("Better Lanterns/Notes/примечания.md", "# Заметки"),
    ];

    /// <summary>
    /// Puts the mod in the library, with its files on disk.
    /// </summary>
    /// <returns>The library's folder.</returns>
    public static Task<string> StoreAsync(InProcessCli cli, CancellationToken cancellationToken) => cli.InAppAsync(
        async services =>
        {
            string root = await services.GetRequiredService<ISettingsService>().GetLibraryRootAsync(cancellationToken);
            ModRef example = new(new GameRef(FakeCatalogSite.SourceId, "game"), "1");
            LibraryMod stored = LibraryMod.Watch(example, "Example Mod", "Someone", FakeCatalogSite.ModLink("1"), DateTimeOffset.UnixEpoch)
                .WithFile(Place(root, example, "12", "fake-catalog/game/1/Lanterns.zip", Zip(Files), DateTimeOffset.UnixEpoch.AddDays(2)))
                .WithFile(Place(root, example, "13", "fake-catalog/game/1/Lanterns.esp", Encoding.UTF8.GetBytes("TES4"), DateTimeOffset.UnixEpoch.AddDays(1)));
            await services.GetRequiredService<ILibraryRepository>().UpdateAsync(example, _ => stored, cancellationToken);
            return root;
        },
        cancellationToken);

    /// <summary>
    /// Gives Example Mod a second archive, file 20, <c>Lanterns Patch.7z</c>, which is damaged.
    /// </summary>
    public static Task AddDamagedAsync(InProcessCli cli, CancellationToken cancellationToken) => cli.InAppAsync(
        async services =>
        {
            string root = await services.GetRequiredService<ISettingsService>().GetLibraryRootAsync(cancellationToken);
            ModRef example = new(new GameRef(FakeCatalogSite.SourceId, "game"), "1");
            LibraryFile patch = Place(root, example, "20", "fake-catalog/game/1/Lanterns Patch.7z", Encoding.UTF8.GetBytes("not a 7z archive"), DateTimeOffset.UnixEpoch.AddDays(3));
            await services.GetRequiredService<ILibraryRepository>().UpdateAsync(example, mod => mod!.WithFile(patch), cancellationToken);
            return true;
        },
        cancellationToken);

    private static byte[] Zip(IEnumerable<(string Path, string Text)> files)
    {
        using MemoryStream bytes = new();
        using (ZipArchive archive = new(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string text) in files)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return bytes.ToArray();
    }

    private static LibraryFile Place(string root, ModRef mod, string id, string relativePath, byte[] content, DateTimeOffset downloadedAt)
    {
        string path = LibraryPaths.Resolve(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return new LibraryFile(new ModFileRef(mod, id), $"File {id}", null, ModFileCategory.Main, DateTimeOffset.UnixEpoch, relativePath, content.Length, downloadedAt);
    }
}
