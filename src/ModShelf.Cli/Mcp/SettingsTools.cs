using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Settings;
using ModShelf.Cli.Common;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP tools for the settings in <c>settings.json</c>, by the names the CLI's settings command gives them.
/// </summary>
[McpServerToolType]
internal sealed class SettingsTools(ISettingsService settings)
{
    [McpServerTool(Name = "settings_show", Title = "Show the settings", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Shows every setting, the ones left at their defaults too, where they're kept, and what each one's value can be. library.root is where the library is; library.folders, and library.folders.<game> for a game of its own, how its folders are named; library.oldVersions whether a file's old versions stay (all), move to an _archive folder (archive), or are deleted down to a number; updates.interval how often the desktop app checks for updates.")]
    public Task<SettingsListing> ShowAsync(CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            AppSettings stored = await settings.GetAsync(cancellationToken).ConfigureAwait(false);
            return new SettingsListing(settings.Location, SettingChanges.List(stored, settings.DefaultLibraryRoot), SettingChanges.Values);
        });

    [McpServerTool(Name = "settings_set", Title = "Change a setting", Idempotent = true, Destructive = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Changes a setting. Setting library.oldVersions to a number makes later downloads delete a file's older versions down to that many. library.root can change only while the library has no files: moving its files is left to the user.")]
    public Task<SettingResult> SetAsync(
        [Description($"The setting: {SettingChanges.Names}.")] string setting,
        [Description($"Its new value: {SettingChanges.Values}.")] string value,
        CancellationToken cancellationToken)
        => ChangeAsync(setting, value, cancellationToken);

    [McpServerTool(Name = "settings_reset", Title = "Reset a setting", Idempotent = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Sets a setting back to its default; for library.folders.<game>, the game's files go by library.folders again.")]
    public Task<SettingResult> ResetAsync(
        [Description($"The setting: {SettingChanges.Names}.")] string setting,
        CancellationToken cancellationToken)
        => ChangeAsync(setting, null, cancellationToken);

    private Task<SettingResult> ChangeAsync(string setting, string? value, CancellationToken cancellationToken)
        => McpProblems.GuardAsync(async () =>
        {
            try
            {
                string now = await SettingChanges.ChangeAsync(settings, setting, value, cancellationToken).ConfigureAwait(false);
                return new SettingResult(setting.Trim(), now);
            }
            catch (InvalidOperationException exception) when (SettingChanges.IsLibraryRoot(setting))
            {
                throw new McpException(
                    exception.Message + " Moving the library's files is for the user, in the desktop app or with the CLI's library move.",
                    exception);
            }
        });
}
