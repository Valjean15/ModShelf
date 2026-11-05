using System.Globalization;

using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Cli.Commands;

/// <summary>
/// How the commands write times, dates and counts, and the sites' words.
/// </summary>
internal static class CliText
{
    /// <summary>
    /// A moment in local time, to the minute, such as <c>2026-10-29 21:05</c>.
    /// </summary>
    public static string Time(DateTimeOffset moment) => moment.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// A moment's day in local time, such as <c>2026-10-29</c>.
    /// </summary>
    public static string Date(DateTimeOffset moment) => moment.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// A count, with the user's thousands separator.
    /// </summary>
    public static string Count(long count) => count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>
    /// A count of what it counts, such as "1 file" or "1,204 files".
    /// </summary>
    public static string Count(int count, string one, string many)
        => count == 1 ? $"1 {one}" : string.Create(CultureInfo.CurrentCulture, $"{count:N0} {many}");

    /// <summary>
    /// A site's word as it starts a label, such as "endorsements" as "Endorsements".
    /// </summary>
    public static string Capitalized(string word)
        => word.Length == 0 ? word : char.ToUpper(word[0], CultureInfo.CurrentCulture) + word[1..];

    /// <summary>
    /// The site's name, such as Nexus Mods; its ID when no source of that ID is registered.
    /// </summary>
    public static string SiteName(ISourceRegistry sources, SourceId source) => sources.Find<IModSource>(source)?.DisplayName ?? source.Value;

    /// <summary>
    /// A mod as prose names it, such as "mod 12604 of skyrimspecialedition on Nexus Mods": its ID and its game's as they
    /// are in links, and its site by name.
    /// </summary>
    public static string ModOn(ISourceRegistry sources, ModRef mod) => $"mod {mod.Id} of {mod.Game.Key} on {SiteName(sources, mod.Game.Source)}";

    /// <summary>
    /// What the site calls following a mod and approving one; none when no source of that ID is registered.
    /// </summary>
    public static SiteWords Words(ISourceRegistry sources, SourceId source) => sources.Find<IModSource>(source)?.Words ?? SiteWords.None;
}
