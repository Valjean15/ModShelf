using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;
using ModShelf.Application.Sources;
using ModShelf.Cli.Common;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Library;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>settings show</c>, <c>settings set &lt;setting&gt; &lt;value&gt;</c> and <c>settings reset &lt;setting&gt;</c>: the
/// settings in <c>settings.json</c>, by name.
/// </summary>
internal static class SettingsCommand
{
    public static Command Create(CliEnvironment environment)
    {
        Command show = new("show", "Shows every setting, the ones left at their defaults too, and where they're kept.");
        show.SetAction((_, cancellationToken) => ShowAsync(environment, cancellationToken));

        Argument<string> setName = new("setting") { Description = $"The setting: {SettingChanges.Names}." };
        Argument<string> value = new("value")
        {
            Description = $"Its new value: {SettingChanges.Values}.",
        };
        Command set = new("set", "Changes a setting.") { setName, value };
        set.SetAction((parsed, cancellationToken) => ChangeAsync(environment, parsed.GetValue(setName)!, parsed.GetValue(value), cancellationToken));

        Argument<string> resetName = new("setting") { Description = $"The setting: {SettingChanges.Names}." };
        Command reset = new("reset", "Sets a setting back to its default.") { resetName };
        reset.SetAction((parsed, cancellationToken) => ChangeAsync(environment, parsed.GetValue(resetName)!, null, cancellationToken));

        return new Command("settings", "Shows and changes the settings: where the library is, how its folders are named, what happens to old versions, and how often to check for updates and whether to say so, whether the desktop app is light or dark, whether closing its window keeps it running in the tray, whether its first-run setup is done, and whether it looks for a newer ModShelf when it starts.")
        {
            show,
            set,
            reset,
        };
    }

    private static async Task<int> ShowAsync(CliEnvironment environment, CancellationToken cancellationToken)
    {
        return await CommandHost.RunAsync(
                environment,
                async services =>
                {
                    ISettingsService settings = services.GetRequiredService<ISettingsService>();
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
                        .. SettingChanges.List(stored, settings.DefaultLibraryRoot, services.GetRequiredService<IUserFolders>().Downloads)
                            .Select(setting => new[] { setting.Name, setting.IsDefault ? $"{setting.Value} (default)" : setting.Value }),
                    ];
                    TextTable.Write(environment.Output, rows);
                    environment.Output.WriteLine(
                        $"Folder patterns use {FolderPattern.SourceToken}, {FolderPattern.GameToken}, {FolderPattern.ModToken} and "
                        + $"{FolderPattern.ModIdToken}, and end with /{FolderPattern.FileToken}.");
                    environment.Output.WriteLine(
                        $"When a newer version of a file is downloaded, {SettingChanges.OldVersions} keeps the old ones (all), moves them to an "
                        + "_archive folder (archive), or keeps that many versions and deletes the older ones (a number).");
                    environment.Output.WriteLine(
                        $"The desktop app checks the watched mods for updates every {SettingChanges.Interval}, and shows a notice of new ones unless "
                        + $"{SettingChanges.Notify} is false; library check checks them now.");
                    environment.Output.WriteLine($"The desktop app is light or dark as {SettingChanges.Theme} says, or follows the system's mode (system).");
                    environment.Output.WriteLine(
                        $"Closing the desktop app's window keeps ModShelf running in the tray when {SettingChanges.CloseToTray} is true, and quits it "
                        + "when it's false; until it's set, the app asks the first time.");
                    environment.Output.WriteLine(
                        $"The desktop app shows its first-run setup at the start of a new library until {SettingChanges.SetupDone} is true; "
                        + $"settings reset {SettingChanges.SetupDone} shows it again.");
                    environment.Output.WriteLine(
                        $"The installed desktop app looks for a newer ModShelf when it starts unless {SettingChanges.CheckForAppUpdates} is false.");
                    environment.Output.WriteLine("To change a setting: settings set <setting> <value>");
                    return ExitCodes.Done;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <param name="value">The setting's new value, or null to set it back to its default.</param>
    private static async Task<int> ChangeAsync(CliEnvironment environment, string name, string? value, CancellationToken cancellationToken)
    {
        if (!SettingChanges.IsSetting(name))
        {
            await environment.Error.WriteLineAsync($"There's no setting called {name}. Settings: {SettingChanges.Names}.").ConfigureAwait(false);
            return ExitCodes.BadInput;
        }

        try
        {
            return await CommandHost.RunAsync(
                    environment,
                    async services =>
                    {
                        ISettingsService settings = services.GetRequiredService<ISettingsService>();
                        string now;
                        try
                        {
                            now = await SettingChanges.ChangeAsync(
                                    settings,
                                    services.GetRequiredService<IModLibrary>(),
                                    services.GetRequiredService<ISourceRegistry>(),
                                    name,
                                    value,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (InvalidOperationException exception) when (value is not null && SettingChanges.IsLibraryRoot(name))
                        {
                            // A library with files moves with library move; one with downloads under way has to wait for them.
                            IReadOnlyList<LibraryMod> library = await services.GetRequiredService<IModLibrary>().ListAsync(cancellationToken).ConfigureAwait(false);
                            string hint = library.Any(mod => mod.Files.Count > 0) ? $" To move it there: library move \"{value}\"" : string.Empty;
                            await environment.Error.WriteLineAsync(exception.Message + hint).ConfigureAwait(false);
                            return ExitCodes.Failed;
                        }
                        catch (InvalidOperationException exception) when (SettingChanges.GameOfRoot(name) is { } game)
                        {
                            // A game with files moves with library move-game, there or back into the library's folder.
                            IReadOnlyList<LibraryMod> library = await services.GetRequiredService<IModLibrary>().ListAsync(cancellationToken).ConfigureAwait(false);
                            bool hasFiles = library.Any(mod => mod.Files.Count > 0
                                && (string.Equals(mod.Mod.Game.Key, game, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(mod.Mod.Game.ToString(), game, StringComparison.OrdinalIgnoreCase)));
                            string hint = !hasFiles ? string.Empty
                                : value is null ? $" To move them back: library move-game {game}"
                                : $" To move them there: library move-game {game} \"{value}\"";
                            await environment.Error.WriteLineAsync(exception.Message + hint).ConfigureAwait(false);
                            return ExitCodes.Failed;
                        }

                        environment.Output.WriteLine(now);
                        return ExitCodes.Done;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            await environment.Error.WriteLineAsync(exception.MessageForUser()).ConfigureAwait(false);
            return ExitCodes.BadInput;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException)
        {
            await environment.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return ExitCodes.Failed;
        }
    }
}
