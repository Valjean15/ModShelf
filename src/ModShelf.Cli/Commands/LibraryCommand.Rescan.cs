using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Library;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>library rescan [link]</c>: finds the files on disk the library doesn't know, and reads the hashes it lacks.
/// </summary>
internal static partial class LibraryCommand
{
    private static Command CreateRescan(CliEnvironment environment)
    {
        Argument<string?> link = new("link")
        {
            Description = "A link to the mod or one of its files, such as a mod's page on Nexus Mods. Without one, every mod in the library.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        Command rescan = new(
            "rescan",
            "Finds the files in the mods' folders that the library doesn't know, such as ones placed by hand, and adds the ones that match the site's files by name and size; reads the SHA-256 of the library's files that have none. Nothing is moved, renamed or deleted.")
        {
            link,
        };
        rescan.SetAction((parsed, cancellationToken) => RescanAsync(environment, parsed.GetValue(link), cancellationToken));
        return rescan;
    }

    private static async Task<int> RescanAsync(CliEnvironment environment, string? linkText, CancellationToken cancellationToken)
    {
        Uri? link = null;
        if (linkText is not null && !ModLinkArgument.TryRead(environment, linkText, out link))
        {
            return ExitCodes.BadInput;
        }

        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    LibraryRescan rescan;
                    string root;
                    try
                    {
                        rescan = await services.GetRequiredService<ILibraryRescan>().RescanAsync(link, cancellationToken).ConfigureAwait(false);
                        root = await services.GetRequiredService<ISettingsService>().GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidDataException exception)
                    {
                        await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                        return ExitCodes.Failed;
                    }
                    catch (Exception exception) when (LinkProblems.Describe(exception) is { } problem)
                    {
                        await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                        return problem.ExitCode;
                    }

                    WriteRescan(environment.Output, rescan, root);
                    foreach (RescanFailure failure in rescan.Failures)
                    {
                        await environment.Error.WriteLineAsync(
                                $"Couldn't match the files of mod {failure.Mod.Id} of {failure.Mod.Game.Key} on {failure.Mod.Game.Source}: {failure.Reason}")
                            .ConfigureAwait(false);
                    }

                    return rescan.Failures.Count == 0 ? ExitCodes.Done : ExitCodes.Failed;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// What a rescan did: the files it added, as a table, then the counts and paths of the rest.
    /// </summary>
    private static void WriteRescan(TextWriter output, LibraryRescan rescan, string root)
    {
        output.WriteLine($"Rescanned {CliText.Count(rescan.Mods, "mod", "mods")}.");
        if (rescan.Added.Count + rescan.Hashed.Count + rescan.Missing.Count + rescan.Unmatched.Count == 0)
        {
            output.WriteLine("Nothing new: every file in the mods' folders is in the library, and every file has its SHA-256.");
            return;
        }

        if (rescan.Added.Count > 0)
        {
            output.WriteLine($"Added {CliText.Count(rescan.Added.Count, "file", "files")} found on disk:");
            string[][] rows =
            [
                ["Game", "ID", "File", "Version", "Path"],
                .. rescan.Added.Select(file => new[]
                {
                    file.File.Mod.Game.Key,
                    file.File.Mod.Id,
                    file.FileName,
                    file.VersionLabel ?? "-",
                    LibraryPaths.Resolve(root, file.RelativePath),
                }),
            ];
            TextTable.Write(output, rows);
        }

        if (rescan.Hashed.Count > 0)
        {
            output.WriteLine($"Read the SHA-256 of {CliText.Count(rescan.Hashed.Count, "file", "files")}.");
        }

        if (rescan.Missing.Count > 0)
        {
            output.WriteLine(rescan.Missing.Count == 1
                ? "1 file is missing from disk:"
                : $"{CliText.Count(rescan.Missing.Count, "file", "files")} are missing from disk:");
            foreach (LibraryFile file in rescan.Missing)
            {
                output.WriteLine($"  {LibraryPaths.Resolve(root, file.RelativePath)}");
            }
        }

        if (rescan.Unmatched.Count > 0)
        {
            output.WriteLine(rescan.Unmatched.Count == 1
                ? "1 file in the mods' folders matches none of their files:"
                : $"{CliText.Count(rescan.Unmatched.Count, "file", "files")} in the mods' folders match none of their files:");
            foreach (string path in rescan.Unmatched)
            {
                output.WriteLine($"  {path}");
            }
        }
    }
}
