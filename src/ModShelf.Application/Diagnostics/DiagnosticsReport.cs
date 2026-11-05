using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Diagnostics.Ports;
using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Domain.Common;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;

namespace ModShelf.Application.Diagnostics;

/// <summary>
/// Writes the diagnostics report from what the app knows: its version and system, its folders, the settings, the
/// library, the queue, the linked accounts, and the deprecated arguments callers still use.
/// </summary>
internal sealed partial class DiagnosticsReport(
    AppInfo app,
    IAppFolders folders,
    ISettingsService settings,
    IModLibrary library,
    IDownloadService downloads,
    IAccountService accounts,
    ISecretStore secrets,
    IDeprecatedCalls deprecatedCalls,
    TimeProvider timeProvider,
    ILogger<DiagnosticsReport> logger) : IDiagnostics
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public async Task<string> DescribeAsync(CancellationToken cancellationToken)
    {
        StringBuilder report = new();
        void Line(string text) => report.Append(text).Append('\n');

        Line($"ModShelf {app}");
        Line(Text($"Written: {timeProvider.GetUtcNow():yyyy-MM-dd HH:mm} UTC"));
        Line($"System: {RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture}, {RuntimeInformation.FrameworkDescription}");
        Line(await PartAsync("Folders", async () =>
            Describe(folders, await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false))).ConfigureAwait(false));
        Line(await PartAsync("Settings", async () => Describe(await settings.GetAsync(cancellationToken).ConfigureAwait(false))).ConfigureAwait(false));
        Line(await PartAsync("Library", async () => Describe(await library.ListAsync(cancellationToken).ConfigureAwait(false))).ConfigureAwait(false));
        Line(await PartAsync("Downloads", async () => Describe(await downloads.ListAsync(cancellationToken).ConfigureAwait(false))).ConfigureAwait(false));
        Line(await PartAsync("Accounts", async () => Describe(await accounts.ListAsync(cancellationToken).ConfigureAwait(false))).ConfigureAwait(false));
        Line(await PartAsync("Legacy arguments", async () => Describe(await deprecatedCalls.ListAsync(cancellationToken).ConfigureAwait(false))).ConfigureAwait(false));
        return report.ToString();

        // A part that can't be read names the error's type only: its message could hold a path or a name.
        async Task<string> PartAsync(string name, Func<Task<string>> describe)
        {
            try
            {
                return $"{name}: {await describe().ConfigureAwait(false)}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPartUnreadable(logger, exception, name);
                return $"{name}: couldn't be read ({exception.GetType().Name})";
            }
        }
    }

    /// <summary>
    /// The games' own folders are counted, not shown: there may be many.
    /// </summary>
    private static string Describe(IAppFolders folders, LibraryRoots roots)
        => $"data {Tilde(folders.DataFolder)}, logs {Tilde(folders.LogFolder)}, library {Tilde(roots.Root)}"
            + (roots.Games.Count == 0 ? string.Empty : $", {Count(roots.Games.Count, "game")} in folders of their own");

    private static string Describe(AppSettings stored)
    {
        TimeSpan interval = stored.UpdateInterval ?? UpdateInterval.Default;
        string close = stored.CloseToTray switch
        {
            true => "keeps running in the tray",
            false => "quits",
            null => "asks",
        };

        // A game's settings besides its folders, such as its own interval, are counted apart, when there are any.
        int folders = stored.Games.Values.Count(game => game.Folders is not null);
        int others = stored.Games.Values.Count(game => !(game with { Folders = null }).IsNone);
        return string.Join(
            ", ",
            [
                $"check for updates every {Describe(interval)}",
                Describe(stored.OldVersions ?? OldVersionRule.Default),
                $"theme {stored.Theme ?? AppTheme.System}",
                $"notices {(stored.NotifyOfUpdates ?? true ? "on" : "off")}",
                $"closing the window {close}",
                $"folders for {Count(folders, "game")} set apart",
                .. others == 0 ? Array.Empty<string>() : [$"other settings for {Count(others, "game")} set apart"],
            ]);
    }

    private static string Describe(TimeSpan interval) => interval switch
    {
        { TotalDays: >= 1 } when interval.TotalDays % 1 == 0 => Count(interval.TotalDays, "day"),
        { TotalHours: >= 1 } when interval.TotalHours % 1 == 0 => Count(interval.TotalHours, "hour"),
        _ => Count(interval.TotalMinutes, "minute"),
    };

    private static string Describe(OldVersionRule rule) => rule.Action switch
    {
        OldVersionAction.KeepLatest => $"keep the last {Count(rule.Count, "version")}",
        OldVersionAction.Archive => "archive old versions",
        _ => "keep every version",
    };

    private static string Describe(IReadOnlyList<LibraryMod> mods)
    {
        int files = mods.Sum(mod => mod.Files.Count);
        int updates = mods.Sum(mod => mod.Updates.Count);
        DateTimeOffset? oldestCheck = mods.Where(mod => mod.IsWatched).Min(mod => mod.CheckedAt);
        string check = oldestCheck is { } at ? Text($"every watched mod checked on {at.ToUniversalTime():yyyy-MM-dd HH:mm} UTC or later") : "no watched mod checked yet";
        return $"{Count(mods.Count, "mod")}, {mods.Count(mod => mod.IsWatched).ToString(CultureInfo.InvariantCulture)} watched, {Count(files, "file")}, {Count(updates, "update")}; {check}";
    }

    private static string Describe(IReadOnlyList<DownloadJobSnapshot> jobs)
        => jobs.Count == 0
            ? "none"
            : string.Join(", ", jobs.GroupBy(job => job.Status).OrderBy(group => group.Key).Select(group => Text($"{group.Count()} {group.Key}")));

    private string Describe(IReadOnlyList<SourceAccountStatus> sites)
    {
        // The site's ID, how the account is linked and its tier; never the account's name.
        IEnumerable<string> each = sites.Select(site => site.Account is { } account
            ? $"{site.Source} linked ({account.Kind}{(account.Profile.Tier is { } tier ? $", {tier}" : string.Empty)})"
            : $"{site.Source} not linked");
        string kept = secrets.IsProtectedByOs ? "secrets kept by the system" : "secrets in files encrypted for this computer, no keyring";
        return $"{string.Join("; ", each)}; {kept}";
    }

    /// <summary>
    /// Such as <c>download.link ×3 (last 2026-11-03 22:31 UTC, claude-ai)</c>, by name; <c>none</c> when no call is
    /// counted. What the count's file says is kept to a line.
    /// </summary>
    private static string Describe(IReadOnlyList<DeprecatedCall> calls)
        => calls.Count == 0
            ? "none"
            : string.Join(
                "; ",
                calls
                    .OrderBy(call => call.Name, StringComparer.Ordinal)
                    .Select(call => Text(
                        $"{DeprecatedCalls.OneLine(call.Name, DeprecatedCalls.LongestClient)} ×{call.Count} (last {call.Last.ToUniversalTime():yyyy-MM-dd HH:mm} UTC{(DeprecatedCalls.OneLine(call.Client, DeprecatedCalls.LongestClient) is { } client ? $", {client}" : string.Empty)})")));

    /// <summary>
    /// <paramref name="path"/> with the user's home folder as <c>~</c>, which leaves the user's name out.
    /// </summary>
    private static string Tilde(string path)
    {
        if (string.IsNullOrEmpty(Home))
        {
            return path;
        }

        string home = Path.TrimEndingDirectorySeparator(Home);
        return FilePaths.Same(path, home)
            ? "~"
            : path.StartsWith(home + Path.DirectorySeparatorChar, FilePaths.Comparison) ? "~" + path[home.Length..] : path;
    }

    /// <summary>
    /// Such as <c>1 hour</c> or <c>6 hours</c>.
    /// </summary>
    private static string Count(double count, string word) => Text($"{count} {word}{(count == 1 ? string.Empty : "s")}");

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
