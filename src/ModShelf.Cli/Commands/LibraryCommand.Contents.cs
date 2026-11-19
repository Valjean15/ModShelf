using System.CommandLine;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Contents;
using ModShelf.Cli.Common;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Library;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>library contents &lt;link&gt; [--file &lt;name&gt;] [--read &lt;entry&gt;]</c>: what one of the library's archives holds,
/// read where it is, extracting nothing (ADR 0106).
/// </summary>
internal static partial class LibraryCommand
{
    private static Command CreateContents(CliEnvironment environment)
    {
        Argument<string> link = new("link")
        {
            Description = "A link to the mod or one of its files, such as a mod's page on Nexus Mods.",
        };
        Option<string?> file = new("--file")
        {
            Description = "The archive, by its file name or its ID on the site, when the mod has more than one in the library.",
        };
        Option<string?> read = new("--read")
        {
            Description = "A text file in the archive, by its path as the listing gives it, such as a readme: shows its text instead.",
        };

        Command command = new("contents", "Lists the folders and files in one of the library's archives (.zip, .7z or .rar), with their sizes, or shows a text file in it, such as its readme. Nothing is extracted.")
        {
            link,
            file,
            read,
        };
        command.SetAction((parsed, cancellationToken) => ContentsAsync(environment, parsed.GetValue(link)!, parsed.GetValue(file), parsed.GetValue(read), cancellationToken));
        return command;
    }

    private static async Task<int> ContentsAsync(CliEnvironment environment, string linkText, string? fileName, string? entry, CancellationToken cancellationToken)
    {
        if (!ModLinkArgument.TryRead(environment, linkText, out Uri? link))
        {
            return ExitCodes.BadInput;
        }

        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    ILibraryContents contents = services.GetRequiredService<ILibraryContents>();
                    try
                    {
                        LibraryMod mod = await contents.FindModAsync(link, cancellationToken).ConfigureAwait(false);
                        if (await PickArchiveAsync(environment, contents, mod, fileName).ConfigureAwait(false) is not { } archive)
                        {
                            return ExitCodes.BadInput;
                        }

                        if (entry is not null)
                        {
                            ArchiveText text = await contents.ReadTextAsync(archive.File, entry, cancellationToken).ConfigureAwait(false);
                            await environment.Output.WriteAsync(text.Text).ConfigureAwait(false);
                            await environment.Output.WriteLineAsync().ConfigureAwait(false);
                            if (text.IsCut)
                            {
                                await environment.Error.WriteLineAsync(
                                        $"That's the first {Sizes.Format(ILibraryContents.MaxTextBytes)} of {text.Entry}, which is {Sizes.Format(text.SizeInBytes)}.")
                                    .ConfigureAwait(false);
                            }

                            return ExitCodes.Done;
                        }

                        WriteContents(environment.Output, await contents.ListAsync(archive.File, cancellationToken).ConfigureAwait(false), linkText);
                        return ExitCodes.Done;
                    }
                    catch (Exception exception) when (exception is KeyNotFoundException or FileNotFoundException or ArchiveUnreadableException)
                    {
                        await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                        return ExitCodes.Failed;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        await environment.Error.WriteLineAsync($"The archive couldn't be read: {exception.Message}").ConfigureAwait(false);
                        return ExitCodes.Failed;
                    }
                    catch (Exception exception) when (LinkProblems.Describe(exception) is { } problem)
                    {
                        await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                        return problem.ExitCode;
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The mod's archive <paramref name="fileName"/> names, by its file name or its ID, or its only one; null, having
    /// said why, when there's none, or more than one and none named.
    /// </summary>
    private static async Task<LibraryFile?> PickArchiveAsync(CliEnvironment environment, ILibraryContents contents, LibraryMod mod, string? fileName)
    {
        LibraryFile[] archives = ModArchives.Of(mod, contents);
        if (ModArchives.Pick(archives, fileName) is { } picked)
        {
            return picked;
        }

        if (fileName is not null)
        {
            await environment.Error.WriteLineAsync($"{mod.Name} has no archive in the library called {fileName.Trim()}.").ConfigureAwait(false);
        }
        else if (archives.Length == 0)
        {
            await environment.Error.WriteLineAsync(
                    $"{mod.Name} has no archive in the library to look into: ModShelf reads .zip, .7z and .rar files.")
                .ConfigureAwait(false);
            return null;
        }
        else
        {
            await environment.Error.WriteLineAsync($"{mod.Name} has {archives.Length.ToString(CultureInfo.CurrentCulture)} archives in the library. Name one with --file:")
                .ConfigureAwait(false);
        }

        if (archives.Length > 0)
        {
            TextTable.Write(environment.Error, [["File", "ID", "Size"], .. archives.Select(own => new[] { own.FileName, own.File.Id, Sizes.Format(own.SizeInBytes) })]);
        }

        return null;
    }

    /// <summary>
    /// The archive's folders and files as a tree, each file with its size, then its readme, if it has one.
    /// </summary>
    private static void WriteContents(TextWriter output, ArchiveContents contents, string link)
    {
        int folders = contents.Entries.Count - contents.Files;
        output.WriteLine(string.Create(
            CultureInfo.CurrentCulture,
            $"{contents.FileName}, a {contents.Format} archive: {Count(contents.Files, "file", "files")} in {Count(folders, "folder", "folders")}, {Sizes.Format(contents.UnpackedSize)} unpacked."));
        output.WriteLine();
        foreach (ArchiveEntry entry in contents.Entries)
        {
            string indent = new(' ', entry.Depth * 2);
            output.WriteLine(entry.IsFolder ? $"{indent}{entry.Name}/" : $"{indent}{entry.Name}  {Sizes.Format(entry.SizeInBytes)}");
        }

        if (contents.Readme is { } readme)
        {
            output.WriteLine();
            output.WriteLine($"Its readme is {readme}. To show it: library contents \"{link}\" --file \"{contents.FileName}\" --read \"{readme}\"");
        }
    }
}
