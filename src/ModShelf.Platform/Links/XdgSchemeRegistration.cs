using ModShelf.Application.Links;
using ModShelf.Application.Links.Ports;

namespace ModShelf.Platform.Links;

/// <summary>
/// Link schemes on Linux and other freedesktop.org systems: a desktop entry of ModShelf's own for the scheme, such as
/// <c>~/.local/share/applications/modshelf-nxm.desktop</c>, made the default for <c>x-scheme-handler/nxm</c> in the
/// user's <c>~/.config/mimeapps.list</c>. What that default was is kept in the entry, as <c>X-ModShelf-Previous</c>, and
/// put back when ModShelf gives the links back.
/// </summary>
/// <remarks>
/// Which app the system opens the links with follows the Association between MIME types and applications
/// specification: the <c>mimeapps.list</c> files in order, desktop-specific ones first, then the first application
/// that says it opens them.
/// </remarks>
internal sealed class XdgSchemeRegistration(XdgDirectories directories, SchemeHandlerOptions options) : ISchemeRegistration
{
    private const string Entry = "Desktop Entry";
    private const string PreviousKey = "X-ModShelf-Previous";

    private string UserList => Path.Combine(directories.ConfigHome, "mimeapps.list");

    public Task<SchemeState> GetAsync(string scheme, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        string ours = OurId(scheme);
        string? current = DefaultFor(Mime(scheme), userValue: null, excluded: null);
        SchemeState state = current == ours
            ? new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: NameOf(DefaultFor(Mime(scheme), Stored(scheme) ?? string.Empty, excluded: ours)))
            : !File.Exists(options.DesktopApp) ? SchemeState.Unavailable
            : current is null ? SchemeState.None
            : new SchemeState(SchemeHandlerKind.OtherApp, NameOf(current));
        return Task.FromResult(state);
    }

    public Task RegisterAsync(string scheme, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        if (!File.Exists(options.DesktopApp))
        {
            throw new InvalidOperationException($"The desktop app isn't at {options.DesktopApp}, so links can't be opened with it.");
        }

        string ours = OurId(scheme);
        string? userValue = MimeAppsList.Read(UserList, MimeAppsList.DefaultApplications, Mime(scheme));

        // Registered already: what was put aside the first time stays. Otherwise the user's own default is put aside.
        string previous = File.Exists(OurFile(scheme)) && FirstId(userValue) == ours
            ? Stored(scheme) ?? string.Empty
            : FirstId(userValue) == ours ? string.Empty : userValue ?? string.Empty;

        Directory.CreateDirectory(Path.GetDirectoryName(OurFile(scheme))!);
        string temporary = OurFile(scheme) + ".modshelf-new";
        File.WriteAllText(
            temporary,
            string.Join(
                '\n',
                $"[{Entry}]",
                "Type=Application",
                "Name=ModShelf",
                $"Comment=Opens {scheme}:// links in ModShelf",
                $"Exec={DesktopEntryExec.ForLink(options.DesktopApp, options.Arguments)}",
                "Terminal=false",
                "NoDisplay=true",
                $"MimeType={Mime(scheme)};",
                $"{PreviousKey}={previous}",
                string.Empty));
        File.Move(temporary, OurFile(scheme), overwrite: true);
        MimeAppsList.Write(UserList, MimeAppsList.DefaultApplications, Mime(scheme), ours + ";");
        return Task.CompletedTask;
    }

    public Task UnregisterAsync(string scheme, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        // Another app made the default since: it stays.
        if (FirstId(MimeAppsList.Read(UserList, MimeAppsList.DefaultApplications, Mime(scheme))) == OurId(scheme))
        {
            string? previous = Stored(scheme);
            MimeAppsList.Write(UserList, MimeAppsList.DefaultApplications, Mime(scheme), string.IsNullOrEmpty(previous) ? null : previous);
        }

        if (File.Exists(OurFile(scheme)))
        {
            File.Delete(OurFile(scheme));
        }

        return Task.CompletedTask;
    }

    private static string Mime(string scheme) => $"x-scheme-handler/{scheme}";

    private static string OurId(string scheme) => $"modshelf-{scheme}.desktop";

    private static string? FirstId(string? value)
        => value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    private string OurFile(string scheme) => Path.Combine(directories.DataHome, "applications", OurId(scheme));

    /// <summary>
    /// The user's default put aside in ModShelf's entry: empty for none, null without the entry.
    /// </summary>
    private string? Stored(string scheme) => MimeAppsList.Read(OurFile(scheme), Entry, PreviousKey);

    /// <summary>
    /// The desktop entry the system opens <paramref name="mime"/> with.
    /// </summary>
    /// <param name="userValue">What to read in the user's own <c>mimeapps.list</c> in place of its value; null for the value as it is.</param>
    /// <param name="excluded">A desktop entry to pass over, as if it weren't there.</param>
    private string? DefaultFor(string mime, string? userValue, string? excluded)
    {
        foreach (string list in Lists())
        {
            string? value = userValue is not null && list == UserList ? userValue : MimeAppsList.Read(list, MimeAppsList.DefaultApplications, mime);
            foreach (string id in value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
            {
                if (id != excluded && Find(id) is not null)
                {
                    return id;
                }
            }
        }

        // No default: the first application that says it opens them.
        foreach (string folder in ApplicationFolders().Where(Directory.Exists))
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.desktop").Order(StringComparer.Ordinal))
            {
                string id = Path.GetFileName(file);
                if (id != excluded
                    && !IsHidden(file)
                    && (MimeAppsList.Read(file, Entry, "MimeType") ?? string.Empty).Split(';', StringSplitOptions.TrimEntries).Contains(mime))
                {
                    return id;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The <c>mimeapps.list</c> files, most important first.
    /// </summary>
    private IEnumerable<string> Lists()
    {
        IEnumerable<string> In(string folder)
            => [.. directories.Desktops.Select(desktop => Path.Combine(folder, $"{desktop}-mimeapps.list")), Path.Combine(folder, "mimeapps.list")];

        return [
            .. In(directories.ConfigHome),
            .. directories.ConfigDirs.SelectMany(In),
            .. In(Path.Combine(directories.DataHome, "applications")),
            .. directories.DataDirs.SelectMany(folder => In(Path.Combine(folder, "applications"))),
        ];
    }

    private IEnumerable<string> ApplicationFolders()
        => [Path.Combine(directories.DataHome, "applications"), .. directories.DataDirs.Select(folder => Path.Combine(folder, "applications"))];

    /// <summary>
    /// The file of a desktop entry, by its ID, such as <c>vortex.desktop</c>, or <c>kde-dolphin.desktop</c> for
    /// <c>kde/dolphin.desktop</c>; null when there's none, or it's hidden.
    /// </summary>
    private string? Find(string id)
    {
        int dash = id.IndexOf('-', StringComparison.Ordinal);
        foreach (string folder in ApplicationFolders())
        {
            foreach (string file in dash > 0 ? [Path.Combine(folder, id), Path.Combine(folder, id[..dash], id[(dash + 1)..])] : new[] { Path.Combine(folder, id) })
            {
                if (File.Exists(file))
                {
                    return IsHidden(file) ? null : file;
                }
            }
        }

        return null;
    }

    private static bool IsHidden(string file) => string.Equals(MimeAppsList.Read(file, Entry, "Hidden"), "true", StringComparison.Ordinal);

    /// <summary>
    /// The application's name, as its desktop entry gives it, such as <c>Vortex</c>.
    /// </summary>
    private string? NameOf(string? id)
        => id is null ? null : (Find(id) is { } file ? MimeAppsList.Read(file, Entry, "Name") : null) ?? Path.GetFileNameWithoutExtension(id);
}
