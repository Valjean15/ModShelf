using ModShelf.Application.Links;
using ModShelf.Application.Links.Ports;

namespace ModShelf.Platform.Links;

/// <summary>
/// Link schemes in the Windows registry: a scheme's class under the current user's <c>Software\Classes</c>, as Vortex
/// and Mod Organizer 2 register <c>nxm</c> too. The class ModShelf replaces is copied to
/// <c>Software\ModShelf\Previous</c> first, and copied back when ModShelf gives the links back.
/// </summary>
/// <remarks>
/// An app the user chose in Windows' own "open with" prompt (<c>UrlAssociations\…\UserChoice</c>) wins over the class.
/// ModShelf can't change that choice, so it only names that app.
/// </remarks>
internal sealed class WindowsSchemeRegistration(IRegistryTree user, IRegistryTree machine, SchemeHandlerOptions options)
    : ISchemeRegistration
{
    /// <summary>
    /// The value that marks a class as ModShelf's, holding the desktop app's path.
    /// </summary>
    private const string Marker = "ModShelf";

    private const string PreviousRoot = @"Software\ModShelf\Previous";

    public Task<SchemeState> GetAsync(string scheme, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        string? chosen = ChosenApp(scheme);
        SchemeState state = IsOurs(scheme)
            ? chosen is null ? new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: PreviousApp(scheme)) : new SchemeState(SchemeHandlerKind.OtherApp, chosen)
            : !File.Exists(options.DesktopApp) ? SchemeState.Unavailable
            : (chosen ?? AppOf(user, Class(scheme)) ?? AppOf(machine, Class(scheme))) is { } other ? new SchemeState(SchemeHandlerKind.OtherApp, other)
            : SchemeState.None;
        return Task.FromResult(state);
    }

    public Task RegisterAsync(string scheme, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        if (!File.Exists(options.DesktopApp))
        {
            throw new InvalidOperationException($"The desktop app isn't at {options.DesktopApp}, so links can't be opened with it.");
        }

        string key = Class(scheme);
        if (!IsOurs(scheme))
        {
            // What was there goes aside, as it was, for giving the links back; another app's class goes entirely.
            user.DeleteTree(Previous(scheme));
            if (user.Exists(key))
            {
                user.CopyTree(key, Previous(scheme));
            }

            user.DeleteTree(key);
        }

        user.SetString(key, null, $"URL:{scheme} Protocol");
        user.SetString(key, "URL Protocol", string.Empty);
        user.SetString(key, Marker, options.DesktopApp);
        user.SetString($@"{key}\DefaultIcon", null, $"{WindowsCommandLine.Quote(options.DesktopApp)},0");
        user.SetString($@"{key}\shell\open\command", null, Command());
        return Task.CompletedTask;
    }

    public Task UnregisterAsync(string scheme, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        // Another app that took the links since keeps them; only what was put aside goes.
        if (IsOurs(scheme))
        {
            user.DeleteTree(Class(scheme));
            if (user.Exists(Previous(scheme)))
            {
                user.CopyTree(Previous(scheme), Class(scheme));
            }
        }

        user.DeleteTree(Previous(scheme));
        user.DeleteIfEmpty(PreviousRoot);
        user.DeleteIfEmpty(@"Software\ModShelf");
        return Task.CompletedTask;
    }

    private static string Class(string scheme) => $@"Software\Classes\{scheme}";

    private static string Previous(string scheme) => $@"{PreviousRoot}\{scheme}";

    /// <summary>
    /// The name of the app a class opens links with, from its command; null when it has none.
    /// </summary>
    private static string? AppOf(IRegistryTree tree, string key) => WindowsCommandLine.AppName(tree.GetString($@"{key}\shell\open\command", null));

    private bool IsOurs(string scheme) => user.GetString(Class(scheme), Marker) is not null;

    /// <summary>
    /// The app put aside for the links, or else the one the whole machine has for them, which opens them again once the
    /// current user's class is gone.
    /// </summary>
    private string? PreviousApp(string scheme)
        => user.Exists(Previous(scheme)) ? AppOf(user, Previous(scheme)) : AppOf(machine, Class(scheme));

    /// <summary>
    /// The app the user chose for the scheme in Windows' "open with" prompt, when it isn't the scheme's own class.
    /// </summary>
    private string? ChosenApp(string scheme)
    {
        string? progId = user.GetString($@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}\UserChoice", "ProgId");
        if (string.IsNullOrWhiteSpace(progId) || string.Equals(progId, scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // A name the app registered, unless it's a reference to a resource, such as "@C:\…\app.dll,-101".
        string? named = user.GetString($@"{Class(progId)}\Application", "ApplicationName") ?? machine.GetString($@"{Class(progId)}\Application", "ApplicationName");
        return named is { Length: > 0 } && named[0] != '@' ? named : AppOf(user, Class(progId)) ?? AppOf(machine, Class(progId)) ?? progId;
    }

    /// <summary>
    /// Such as <c>"C:\…\ModShelf.Desktop.exe" "--ModShelf:DatabasePath=…" "%1"</c>.
    /// </summary>
    private string Command()
        => string.Join(' ', [WindowsCommandLine.Quote(options.DesktopApp), .. options.Arguments.Select(WindowsCommandLine.Quote), "\"%1\""]);
}
