using System.ComponentModel;
using System.Globalization;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Contents;
using ModShelf.Cli.Common;
using ModShelf.Domain.Library;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP tool that shows what one of the library's archives holds, read where it is: nothing is extracted or changed
/// (ADR 0106).
/// </summary>
[McpServerToolType]
internal sealed class ContentsTools(ILibraryContents contents)
{
    // The most entries a page lists.
    private const int MaxLimit = 2000;

    // The most of a text file given, in characters, so a long one doesn't fill the agent's context.
    private const int MaxTextCharacters = 32_000;

    [McpServerTool(Name = "library_contents", Title = "Show what an archive holds", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the folders and files in one of the library's downloaded archives (.zip, .7z or .rar), with their sizes once unpacked, a page at a time (offset and limit; hasMore says whether there's another page), and gives its readme's text, or another text file's when read names it, such as an .ini or a .txt. The archive is read where it is: nothing is extracted, installed or changed, and an archive or a file that needs a password, or a damaged one, is reported, not retried. A mod with more than one archive in the library needs file to name one.")]
    public Task<ContentsListing> ListAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods, GameBanana, mod.io, DEG Mods, Thunderstore or CurseForge.")] string link,
        [Description("The archive, by its file name or its ID on the site, when the mod has more than one in the library; library_list gives them.")] string? file = null,
        [Description("A text file in the archive to give, by its path as listed, in place of the readme.")] string? read = null,
        [Description("How many entries to skip: the offset of the page.")] int offset = 0,
        [Description("How many entries a page lists, from 1 to 2000.")] int limit = 500,
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

            try
            {
                LibraryMod mod = await contents.FindModAsync(McpProblems.Link(link), cancellationToken).ConfigureAwait(false);
                LibraryFile[] archives = ModArchives.Of(mod, contents);
                LibraryFile archive = ModArchives.Pick(archives, file) ?? throw new McpException(Unpicked(mod, archives, file));
                ArchiveContents listed = await contents.ListAsync(archive.File, cancellationToken).ConfigureAwait(false);
                string? entry = string.IsNullOrWhiteSpace(read) ? listed.Readme : read;
                ContentsText? text = entry is null ? null : Text(await contents.ReadTextAsync(archive.File, entry, cancellationToken).ConfigureAwait(false));
                int folders = listed.Entries.Count - listed.Files;
                ContentsEntry[] page = [.. listed.Entries.Skip(offset).Take(limit).Select(found => new ContentsEntry(found.Path, found.SizeInBytes, found.IsFolder))];
                string message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{listed.FileName}, a {listed.Format} archive, holds {listed.Files} files in {folders} folders, {listed.UnpackedSize} bytes unpacked.")
                    + (listed.Readme is { } readme ? $" Its readme is {readme}." : " It has no readme.");
                return new ContentsListing(
                    message,
                    listed.FileName,
                    listed.Format,
                    listed.Files,
                    folders,
                    listed.UnpackedSize,
                    listed.Readme,
                    listed.Entries.Count,
                    offset,
                    offset + page.Length < listed.Entries.Count,
                    page,
                    text);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or ArchiveUnreadableException)
            {
                throw new McpException(exception.Message, exception);
            }
        });

    private static ContentsText Text(ArchiveText text)
        => text.Text.Length > MaxTextCharacters
            ? new ContentsText(text.Entry, text.Text[..MaxTextCharacters], text.SizeInBytes, IsCut: true)
            : new ContentsText(text.Entry, text.Text, text.SizeInBytes, text.IsCut);

    private static string Unpicked(LibraryMod mod, LibraryFile[] archives, string? file) => archives.Length switch
    {
        0 => $"{mod.Name} has no archive in the library to look into: ModShelf reads .zip, .7z and .rar files.",
        _ when file is not null => $"{mod.Name} has no archive in the library called {file.Trim()}. Its archives: {Names(archives)}.",
        _ => $"{mod.Name} has {archives.Length} archives in the library: name one with file. Its archives: {Names(archives)}.",
    };

    private static string Names(IEnumerable<LibraryFile> archives) => string.Join(", ", archives.Select(archive => $"{archive.FileName} (ID {archive.File.Id})"));
}
