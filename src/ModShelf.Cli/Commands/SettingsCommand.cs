using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>settings show</c>, <c>settings set &lt;setting&gt; &lt;value&gt;</c> and <c>settings reset &lt;setting&gt;</c>: the
/// settings in <c>settings.json</c>, by name.
/// </summary>
internal static class SettingsCommand
{
    private const string LibraryRoot = "library.root";
    private const string LibraryFolders = "library.folders";
    private const string GamePrefix = LibraryFolders + ".";
    private const string OldVersions = "library.oldVersions";
    private const string Interval = "updates.interval";
    private const string Notify = "updates.notify";
    private const string Names = $"{LibraryRoot}, {LibraryFolders}, {GamePrefix}<game>, {OldVersions}, {Interval}, {Notify}";

    public static Command Create(CliEnvironment environment)
    {
        Command show = new("show", "Shows every setting, the ones left at their defaults too, and where they're kept.");
        show.SetAction((_, cancellationToken) => ShowAsync(environment, cancellationToken));

        Argument<string> setName = new("setting") { Description = $"The setting: {Names}." };
        Argument<string> value = new("value")
        {
            Description = "Its new value: a full path for library.root; all, archive or how many versions to keep for "
                + "library.oldVersions; minutes, hours or days, such as 30m, 6h or 1d, for updates.interval; true or false for "
                + "updates.notify; a folder pattern, "
                + "such as \"{source}/{game}/{mod} ({modid})/{file}\", for the others.",
        };
        Command set = new("set", "Changes a setting.") { setName, value };
        set.SetAction((parsed, cancellationToken) => ChangeAsync(environment, parsed.GetValue(setName)!, parsed.GetValue(value), cancellationToken));

        Argument<string> resetName = new("setting") { Description = $"The setting: {Names}." };
        Command reset = new("reset", "Sets a setting back to its default.") { resetName };
        reset.SetAction((parsed, cancellationToken) => ChangeAsync(environment, parsed.GetValue(resetName)!, null, cancellationToken));

        return new Command("settings", "Shows and changes the settings: where the library is, how its folders are named, what happens to old versions, and how often to check for updates and whether to say so.")
        {
            show,
            set,
            reset,
        };
    }

    private static async Task<int> ShowAsync(CliEnvironment environment, CancellationToken cancellationToken)
    {
        using IHost host = environment.BuildHost(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ISettingsService settings = host.Services.GetRequiredService<ISettingsService>();
            AppSettings stored;
            try
            {
                stored = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
                return ExitCodes.Failed;
            }

            environment.Output.WriteLine($"Settings are kept in {settings.Location}.");
            string[][] rows =
            [
                ["Setting", "Value"],
                [LibraryRoot, stored.LibraryRoot ?? $"{settings.DefaultLibraryRoot} (default)"],
                [LibraryFolders, stored.LibraryFolders?.Text ?? $"{FolderPattern.Default.Text} (default)"],
                .. stored.GameFolders
                    .OrderBy(game => game.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(game => new[] { GamePrefix + game.Key, game.Value.Text }),
                [OldVersions, stored.OldVersions?.ToString() ?? $"{OldVersionRule.Default} (default)"],
                [Interval, stored.UpdateInterval is { } every ? UpdateInterval.Format(every) : $"{UpdateInterval.Format(UpdateInterval.Default)} (default)"],
                [Notify, stored.NotifyOfUpdates is { } notify ? Text(notify) : $"{Text(true)} (default)"],
            ];
            TextTable.Write(environment.Output, rows);
            environment.Output.WriteLine(
                $"Folder patterns use {FolderPattern.SourceToken}, {FolderPattern.GameToken}, {FolderPattern.ModToken} and "
                + $"{FolderPattern.ModIdToken}, and end with /{FolderPattern.FileToken}.");
            environment.Output.WriteLine(
                $"When a newer version of a file is downloaded, {OldVersions} keeps the old ones (all), moves them to an "
                + "_archive folder (archive), or keeps that many versions and deletes the older ones (a number).");
            environment.Output.WriteLine(
                $"The desktop app checks the watched mods for updates every {Interval}, and shows a notice of new ones unless "
                + $"{Notify} is false; library check checks them now.");
            environment.Output.WriteLine("To change a setting: settings set <setting> <value>");
            return ExitCodes.Done;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static string Text(bool value) => value ? "true" : "false";

    /// <summary>
    /// What a rule does with old versions, as a sentence.
    /// </summary>
    private static string Describe(OldVersionRule rule) => rule switch
    {
        { Action: OldVersionAction.KeepAll } => "every version of a file stays.",
        { Action: OldVersionAction.Archive } => "when a newer version of a file is downloaded, the old ones move to an _archive folder.",
        { Count: 1 } => "when a newer version of a file is downloaded, the old ones are deleted.",
        _ => $"when a newer version of a file is downloaded, the newest {rule.Count} stay and older ones are deleted.",
    };

    /// <exception cref="ArgumentException"><paramref name="value"/> is neither true nor false.</exception>
    private static bool ParseBool(string value)
        => bool.TryParse(value.Trim(), out bool parsed) ? parsed : throw new ArgumentException($"'{value}' isn't true or false.", nameof(value));

    /// <param name="value">The setting's new value, or null to set it back to its default.</param>
    private static async Task<int> ChangeAsync(CliEnvironment environment, string name, string? value, CancellationToken cancellationToken)
    {
        string setting = name.Trim().ToLowerInvariant();
        string? game = setting.StartsWith(GamePrefix, StringComparison.Ordinal) ? name.Trim()[GamePrefix.Length..] : null;
        bool oldVersions = setting.Equals(OldVersions, StringComparison.OrdinalIgnoreCase);
        if (setting is not (LibraryRoot or LibraryFolders or Interval or Notify) && !oldVersions && string.IsNullOrWhiteSpace(game))
        {
            await environment.Error.WriteLineAsync($"There's no setting called {name}. Settings: {Names}.").ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        using IHost host = environment.BuildHost(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ISettingsService settings = host.Services.GetRequiredService<ISettingsService>();
            if (setting == LibraryRoot)
            {
                try
                {
                    await settings.SetLibraryRootAsync(value, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException exception) when (value is not null)
                {
                    // A library with files moves with library move; one with downloads under way has to wait for them.
                    IReadOnlyList<LibraryMod> library = await host.Services.GetRequiredService<IModLibrary>().ListAsync(cancellationToken).ConfigureAwait(false);
                    string hint = library.Any(mod => mod.Files.Count > 0) ? $" To move it there: library move \"{value}\"" : string.Empty;
                    await environment.Error.WriteLineAsync(exception.Message + hint).ConfigureAwait(false);
                    return ExitCodes.Failed;
                }

                string root = await settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
                environment.Output.WriteLine(value is null ? $"{LibraryRoot} is back to its default, {root}." : $"{LibraryRoot} is now {root}.");
            }
            else if (setting == Interval)
            {
                TimeSpan? interval = value is null ? null : UpdateInterval.Parse(value);
                await settings.SetUpdateIntervalAsync(interval, cancellationToken).ConfigureAwait(false);
                environment.Output.WriteLine(interval is { } every
                    ? $"{Interval} is now {UpdateInterval.Format(every)}."
                    : $"{Interval} is back to its default, {UpdateInterval.Format(UpdateInterval.Default)}.");
            }
            else if (setting == Notify)
            {
                bool? notify = value is null ? null : ParseBool(value);
                await settings.SetNotifyOfUpdatesAsync(notify, cancellationToken).ConfigureAwait(false);
                string what = notify ?? true
                    ? "the desktop app shows a notice when a background check finds new updates."
                    : "the desktop app shows no notice when a background check finds new updates.";
                environment.Output.WriteLine(notify is { } now ? $"{Notify} is now {Text(now)}: {what}" : $"{Notify} is back to its default, {Text(true)}: {what}");
            }
            else if (oldVersions)
            {
                OldVersionRule? rule = value is null ? null : OldVersionRule.Parse(value);
                await settings.SetOldVersionsAsync(rule, cancellationToken).ConfigureAwait(false);
                environment.Output.WriteLine(rule is null
                    ? $"{OldVersions} is back to its default, {OldVersionRule.Default}: {Describe(OldVersionRule.Default)}"
                    : $"{OldVersions} is now {rule}: {Describe(rule)}");
            }
            else
            {
                FolderPattern? pattern = value is null ? null : FolderPattern.Parse(value);
                await settings.SetLibraryFoldersAsync(game, pattern, cancellationToken).ConfigureAwait(false);
                string named = game is null ? LibraryFolders : GamePrefix + game;
                environment.Output.WriteLine((pattern, game) switch
                {
                    (not null, _) => $"{named} is now {pattern.Text}.",
                    (null, null) => $"{LibraryFolders} is back to its default, {FolderPattern.Default.Text}.",
                    (null, _) => $"{named} is gone, so {game}'s files go by {LibraryFolders} again.",
                });
            }

            return ExitCodes.Done;
        }
        catch (ArgumentException exception)
        {
            await environment.Error.WriteLineAsync(exception.MessageForUser()).ConfigureAwait(false);
            return ExitCodes.BadInput;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
        {
            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.Failed;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
