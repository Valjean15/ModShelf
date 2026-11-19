using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Cleanup;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP tool that lists what could go from the library's folders to free up space. It deletes nothing: deleting is
/// for the user, in the desktop app or the command line (ADR 0104).
/// </summary>
[McpServerToolType]
internal sealed class CleanupTools(ILibraryCleanup cleanup, IModLibrary library)
{
    // The most files a page lists.
    private const int MaxLimit = 500;

    [McpServerTool(Name = "library_cleanup", Title = "List what could go to free up space", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists what could go from ModShelf's library folders to free up space, a page at a time (offset and limit; hasMore says whether there's another page), and deletes nothing. The groups: archived, old versions moved to _archive folders; duplicates, library files with the same SHA-256 and size as another, the copy kept (sameAs); unwatched, files of mods no longer watched; and unmatched, files in the mods' folders that the library doesn't have and that match none of the mod's files on its site. A file that may be the only copy left is kept, saying why: its mod left its site, or the site no longer offers it, or couldn't be asked; so is a file that's one of the mod's files on its site, which library_rescan adds. Each mod's site is asked at most once which files it still offers. Deleting is for the user, in the desktop app (Library, Free up space…) or with ms library cleanup --delete.")]
    public Task<CleanupListing> ListAsync(
        [Description("Only this game's files, as the site names it in its links, such as stardewvalley.")] string? game = null,
        [Description("Only this site's files: nexus, gamebanana, modio, degmods or thunderstore.")] string? source = null,
        [Description("Only the files that can go, leaving out the ones kept.")] bool canGoOnly = false,
        [Description("How many files to skip: the offset of the page.")] int offset = 0,
        [Description("How many files a page lists, from 1 to 500.")] int limit = 200,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            if (limit is < 1 or > MaxLimit)
            {
                throw new McpException($"limit must be from 1 to {MaxLimit}.");
            }

            if (offset < 0)
            {
                throw new McpException("offset must be 0 or more.");
            }

            List<CleanupEntry> entries = [];
            if (game is null && source is null)
            {
                entries.AddRange((await cleanup.PlanAsync(null, null, cancellationToken).ConfigureAwait(false)).Entries);
            }
            else
            {
                // Only the games asked about, so no other game's site is asked anything.
                GameRef[] games =
                [
                    .. (await library.ListAsync(cancellationToken).ConfigureAwait(false))
                        .Select(mod => mod.Mod.Game)
                        .Where(found => game is null || string.Equals(found.Key, game.Trim(), StringComparison.OrdinalIgnoreCase))
                        .Where(found => source is null || string.Equals(found.Source.Value, source.Trim(), StringComparison.OrdinalIgnoreCase))
                        .Distinct(AppSettings.GameComparer)
                        .OrderBy(found => found.ToString(), StringComparer.Ordinal),
                ];
                foreach (GameRef one in games)
                {
                    entries.AddRange((await cleanup.PlanAsync(one, null, cancellationToken).ConfigureAwait(false)).Entries);
                }
            }

            CleanupEntry[] listed =
            [
                .. entries.Where(entry => !canGoOnly || entry.CanGo)
                    .OrderBy(entry => entry.Group)
                    .ThenBy(entry => entry.Mod.Game.ToString(), StringComparer.Ordinal)
                    .ThenBy(entry => entry.ModName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.RelativePath, StringComparer.Ordinal),
            ];
            CleanupGroupTotal[] groups =
            [
                .. entries.GroupBy(entry => entry.Group)
                    .OrderBy(group => group.Key)
                    .Select(group => new CleanupGroupTotal(
                        GroupName(group.Key),
                        group.Count(entry => entry.CanGo),
                        group.Where(entry => entry.CanGo).Sum(entry => entry.SizeInBytes),
                        group.Count(entry => !entry.CanGo))),
            ];
            long bytes = groups.Sum(group => group.CanGoBytes);
            int canGo = groups.Sum(group => group.CanGo);
            int kept = groups.Sum(group => group.Kept);
            string message = entries.Count == 0
                ? "Nothing to free up: no archived old versions, duplicates, files of mods not watched or files that match nothing."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{canGo:N0} files, {bytes:N0} bytes, can go; {kept:N0} are kept. This server deletes nothing: the user deletes them in the desktop app (Library, Free up space…) or with ms library cleanup --delete <group> --yes.");
            return new CleanupListing(
                message,
                bytes,
                groups,
                listed.Length,
                offset,
                offset + limit < listed.Length,
                [.. listed.Skip(offset).Take(limit).Select(Entry)]);
        });

    private static string GroupName(CleanupGroup group) => JsonNamingPolicy.CamelCase.ConvertName(group.ToString());

    private static CleanupFileEntry Entry(CleanupEntry entry)
        => new(
            GroupName(entry.Group),
            entry.Mod.Game.Source.Value,
            entry.Mod.Game.Key,
            entry.Mod.Id,
            entry.ModName,
            entry.File?.File.Id,
            entry.File?.VersionLabel,
            entry.Path,
            entry.SizeInBytes,
            entry.SameAs?.Path,
            entry.CanGo,
            entry.Kept is { } why ? JsonNamingPolicy.CamelCase.ConvertName(why.ToString()) : null,
            entry.KeptDetail);
}
