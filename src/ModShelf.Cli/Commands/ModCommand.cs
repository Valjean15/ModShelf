using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Catalog;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>mod show &lt;link&gt; [--all] [--changelog]</c>: shows a mod's details and files as its site lists them, with
/// the link to download each file. <c>mod search [words] --game &lt;game&gt;</c>: lists a game's mods that match, a page
/// at a time, each with the link <c>mod show</c> and <c>download</c> take. <c>mod updates --game &lt;game&gt;</c>: lists
/// the game's mods that changed lately, with their links. <c>mod tracked</c>, <c>mod track &lt;link&gt;</c> and
/// <c>mod untrack &lt;link&gt;</c>: the mods the linked account tracks on the site. Nothing is downloaded or saved.
/// </summary>
internal static partial class ModCommand
{
    // The sections listed without --all, in the order the sites show them.
    private static readonly ModFileCategory[] CurrentSections =
        [ModFileCategory.Main, ModFileCategory.Update, ModFileCategory.Optional, ModFileCategory.Miscellaneous];

    private static readonly ModFileCategory[] OtherSections =
        [ModFileCategory.OldVersion, ModFileCategory.Archived, ModFileCategory.Unknown];

    public static Command Create(CliEnvironment environment)
    {
        Argument<string> link = new("link")
        {
            Description = "A link to a mod or one of its files, such as a mod's page on Nexus Mods.",
        };
        Option<bool> all = new("--all")
        {
            Description = "Also list old versions, archived files and files in no section.",
        };
        Option<bool> changelog = new("--changelog")
        {
            Description = "Also show the mod's changelog.",
        };

        Command show = new("show", "Shows a mod's details and its files, each with the link to download it. Nothing is downloaded.")
        {
            link,
            all,
            changelog,
        };
        show.SetAction((parsed, cancellationToken)
            => ShowAsync(environment, parsed.GetValue(link)!, parsed.GetValue(all), parsed.GetValue(changelog), cancellationToken));

        return new Command("mod", "Looks mods up on the sites ModShelf knows, and tracks them there.")
        {
            show,
            CreateSearch(environment),
            CreateUpdates(environment),
            CreateTracked(environment),
            CreateTrack(environment, track: true),
            CreateTrack(environment, track: false),
        };
    }

    private static async Task<int> ShowAsync(
        CliEnvironment environment,
        string linkText,
        bool all,
        bool changelog,
        CancellationToken cancellationToken)
    {
        if (!ModLinkArgument.TryRead(environment, linkText, out Uri? link))
        {
            return ExitCodes.BadInput;
        }

        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    ModPage page;
                    try
                    {
                        page = await services.GetRequiredService<IModLookup>().LookUpAsync(link, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (LinkProblems.Describe(exception) is { } problem)
                    {
                        await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
                        return problem.ExitCode;
                    }

                    Write(environment.Output, page, all, changelog);
                    return ExitCodes.Done;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void Write(TextWriter output, ModPage page, bool all, bool changelog)
    {
        WriteDetails(output, page);
        WriteFiles(output, page, all);
        if (changelog)
        {
            WriteChangelog(output, page.Changelogs);
        }
    }

    private static void WriteDetails(TextWriter output, ModPage page)
    {
        ModInfo mod = page.Mod;
        output.WriteLine(mod.Name);
        string[][] details =
        [
            ["Game:", page.Game.Name],
            .. mod.Author is { } author ? new[] { new[] { "Author:", author } } : [],
            .. mod.Version is { } version ? new[] { new[] { "Version:", version } } : [],
            .. mod.UpdatedAt is { } updated ? new[] { new[] { "Updated:", CliText.Date(updated) } } : [],
            .. mod.Endorsements is { } endorsements ? new[] { new[] { "Endorsements:", CliText.Count(endorsements) } } : [],
            .. mod.Downloads is { } downloads ? new[] { new[] { "Downloads:", CliText.Count(downloads) } } : [],
            .. mod.PageUrl is { } pageUrl ? new[] { new[] { "Page:", pageUrl.AbsoluteUri } } : [],
        ];
        TextTable.Write(output, details);

        if (mod.Summary is { } summary)
        {
            output.WriteLine(summary);
        }

        if (mod.HasAdultContent)
        {
            output.WriteLine("Adult content.");
        }

        if (!mod.IsAvailable)
        {
            output.WriteLine("The mod is hidden or removed, so its files can't be downloaded.");
        }
    }

    /// <summary>
    /// One table per section, newest first. Old, archived and unsorted files only with --all, unless the link points
    /// to one of them.
    /// </summary>
    private static void WriteFiles(TextWriter output, ModPage page, bool all)
    {
        output.WriteLine();
        if (page.Files.Count == 0)
        {
            output.WriteLine("The site lists no files for this mod.");
            return;
        }

        ModFile? linked = page.Files.FirstOrDefault(file => file.Reference == page.LinkedFile);
        ModFileCategory[] sections =
        [
            .. CurrentSections,
            .. OtherSections.Where(section => all || section == linked?.Category),
        ];

        foreach (ModFileCategory section in sections)
        {
            ModFile[] files =
            [
                .. page.Files
                    .Where(file => file.Category == section)
                    .OrderByDescending(file => file.UploadedAt)
                    .ThenByDescending(file => file.Reference.Id, StringComparer.Ordinal),
            ];
            if (files.Length == 0)
            {
                continue;
            }

            output.WriteLine(SectionTitle(section));
            string[][] rows =
            [
                ["", "ID", "Name", "Version", "Size", "Uploaded", "Link"],
                .. files.Select(file => new[]
                {
                    file == linked ? "*" : "",
                    file.Reference.Id,
                    file.Name,
                    file.VersionLabel ?? "-",
                    file.SizeInBytes is { } size ? Sizes.Format(size) : "-",
                    CliText.Date(file.UploadedAt),
                    file.PageUrl?.AbsoluteUri ?? "-",
                }),
            ];
            TextTable.Write(output, rows);
            output.WriteLine();
        }

        int hidden = page.Files.Count(file => !sections.Contains(file.Category));
        if (hidden > 0)
        {
            output.WriteLine(hidden == 1
                ? "1 more file (an old version, archived or in no section) isn't shown; add --all to list it."
                : $"{hidden} more files (old versions, archived or in no section) aren't shown; add --all to list them.");
        }

        if (linked is not null)
        {
            output.WriteLine("* The file this link points to, which download gets from it.");
        }

        output.WriteLine("To download a file: download \"<its link>\" --to <folder>");
    }

    private static void WriteChangelog(TextWriter output, IReadOnlyList<ModChangelog> changelogs)
    {
        output.WriteLine();
        if (changelogs.Count == 0)
        {
            output.WriteLine("The mod has no changelog.");
            return;
        }

        output.WriteLine("Changelog");
        foreach (ModChangelog changelog in changelogs)
        {
            output.WriteLine(changelog.Version);
            foreach (string change in changelog.Changes)
            {
                output.WriteLine($"  - {change}");
            }
        }
    }

    private static string SectionTitle(ModFileCategory section) => section switch
    {
        ModFileCategory.Main => "Main files",
        ModFileCategory.Update => "Updates",
        ModFileCategory.Optional => "Optional files",
        ModFileCategory.Miscellaneous => "Miscellaneous files",
        ModFileCategory.OldVersion => "Old versions",
        ModFileCategory.Archived => "Archived files",
        _ => "Files in no section",
    };

}
