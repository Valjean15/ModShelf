using System.Collections.Immutable;

using ModShelf.Application.Settings;
using ModShelf.Domain.Common;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// Where the library's files are: the library's root, and the folders of the games that have their own
/// (<see cref="GameSettings.Root"/>). A file's library path is relative to its game's folder.
/// </summary>
public sealed record LibraryRoots
{
    private readonly ImmutableDictionary<GameRef, string> _games;

    /// <param name="root">The library's root, an absolute path.</param>
    /// <param name="games">The games with folders of their own, and their folders, absolute paths.</param>
    /// <exception cref="ArgumentException">A folder isn't an absolute path.</exception>
    public LibraryRoots(string root, IEnumerable<KeyValuePair<GameRef, string>>? games = null)
    {
        EnsureAbsolute(root, nameof(root));
        ImmutableDictionary<GameRef, string> own = (games ?? []).ToImmutableDictionary(AppSettings.GameComparer);
        foreach (string folder in own.Values)
        {
            EnsureAbsolute(folder, nameof(games));
        }

        Root = root;
        _games = own;
    }

    /// <summary>
    /// The library's root: the folder of every game without one of its own.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// The games with folders of their own, by the game in any case, and their folders.
    /// </summary>
    public IReadOnlyDictionary<GameRef, string> Games => _games;

    /// <summary>
    /// The library's folders as <paramref name="settings"/> say: its root, or <paramref name="defaultRoot"/> when none is
    /// set, and each game's own.
    /// </summary>
    public static LibraryRoots Of(AppSettings settings, string defaultRoot)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new LibraryRoots(
            settings.LibraryRoot ?? defaultRoot,
            settings.Games.Where(game => game.Value.Root is not null).Select(game => KeyValuePair.Create(game.Key, game.Value.Root!)));
    }

    /// <summary>
    /// The folder <paramref name="game"/>'s files are in: its own, or the library's root.
    /// </summary>
    public string For(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return _games.TryGetValue(game, out string? own) ? own : Root;
    }

    /// <summary>
    /// Whether <paramref name="game"/> has a folder of its own.
    /// </summary>
    public bool HasOwn(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return _games.ContainsKey(game);
    }

    /// <summary>
    /// The absolute path of <paramref name="game"/>'s file at <paramref name="relativePath"/> in the library.
    /// </summary>
    /// <exception cref="ArgumentException">The path isn't a library path.</exception>
    public string Resolve(GameRef game, string relativePath) => LibraryPaths.Resolve(For(game), relativePath);

    /// <summary>
    /// The absolute path of a library file.
    /// </summary>
    public string Resolve(LibraryFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return Resolve(file.File.Mod.Game, file.RelativePath);
    }

    /// <summary>
    /// Where <paramref name="path"/> is in <paramref name="game"/>'s folder, as a library path; null when it isn't in it.
    /// </summary>
    /// <exception cref="ArgumentException">The path isn't absolute.</exception>
    public string? RelativeTo(GameRef game, string path) => LibraryPaths.RelativeTo(For(game), path);

    /// <summary>
    /// What's wrong with <paramref name="folder"/> as <paramref name="game"/>'s folder, or, with no game, as the
    /// library's root: a sentence when it's the library's or another game's folder, is inside one, or holds one; null
    /// when it's clear of them all. Each file then has one folder, and moving one folder takes no other's files along.
    /// </summary>
    /// <param name="folder">An absolute path, as it's stored.</param>
    internal string? ClashOf(GameRef? game, string folder)
    {
        if (game is not null && ClashOf(folder, Root, "the library's folder") is { } withLibrary)
        {
            return withLibrary;
        }

        foreach ((GameRef other, string otherFolder) in _games.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
        {
            if ((game is null || !AppSettings.GameComparer.Equals(other, game)) && ClashOf(folder, otherFolder, $"{other}'s folder") is { } withGame)
            {
                return withGame;
            }
        }

        return null;
    }

    private static string? ClashOf(string folder, string other, string whose)
    {
        if (FilePaths.Same(folder, other))
        {
            return $"{folder} is {whose}. Choose another folder.";
        }

        if (LibraryPaths.RelativeTo(other, folder) is not null)
        {
            return $"{folder} is inside {whose}, {other}. Choose a folder outside it.";
        }

        return LibraryPaths.RelativeTo(folder, other) is not null ? $"{folder} holds {whose}, {other}. Choose a folder that doesn't." : null;
    }

    private static void EnsureAbsolute(string path, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, paramName);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' isn't an absolute path.", paramName);
        }
    }
}
