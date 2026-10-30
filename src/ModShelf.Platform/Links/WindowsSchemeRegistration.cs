using ModShelf.Application.Links;
using ModShelf.Application.Links.Ports;
using ModShelf.Platform.Resources;

namespace ModShelf.Platform.Links;

/// <summary>
/// Link schemes in the Windows registry: a scheme's class under the current user's <c>Software\Classes</c>, as Vortex
/// and Mod Organizer 2 register <c>nxm</c> too. The class ModShelf replaces is copied to
/// <c>Software\ModShelf\Previous</c> first, and copied back when ModShelf gives the links back.
/// </summary>
/// <remarks>
/// <para>
/// A class is ModShelf's when its <c>ModShelf</c> value names the program its command starts. Vortex and Mod Organizer
/// 2 write their command over the class without removing other values, so a class they took back keeps the value but
/// no longer starts that program, and is theirs.
/// </para>
/// <para>
/// The command is written last, so a registration that stopped halfway leaves a class without one. What was put aside
/// is replaced only by a class that has a command, and a class without one gets what was put aside back.
/// </para>
/// <para>
/// An app the user chose in Windows' own "open with" prompt (<c>UrlAssociations\…\UserChoiceLatest</c> or
/// <c>UserChoice</c>) wins over the class. ModShelf can't change that choice, so it only names that app.
/// </para>
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
        SchemeState state = IsModShelfs(scheme)
            ? chosen is not null ? new SchemeState(SchemeHandlerKind.OtherApp, chosen)
            : IsThisModShelfs(scheme) ? new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: PreviousApp(scheme))
            : new SchemeState(SchemeHandlerKind.OtherApp, Strings.AnotherModShelf)
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
        if (!IsModShelfs(scheme))
        {
            // Another app's class goes aside as it is, without a value another ModShelf left in it. A class without a
            // command, such as one a registration that stopped halfway left, keeps what was put aside before.
            if (HasCommand(user, key))
            {
                user.DeleteTree(Previous(scheme));
                user.CopyTree(key, Previous(scheme));
                user.DeleteValue(Previous(scheme), Marker);
            }

            user.DeleteTree(key);
        }

        user.SetString(key, null, $"URL:{scheme} Protocol");
        user.SetString(key, "URL Protocol", string.Empty);
        user.SetString(key, Marker, options.DesktopApp);
        user.SetString($@"{key}\DefaultIcon", null, $"{WindowsCommandLine.Quote(options.DesktopApp)},0");
        user.SetString(Command(key), null, CommandLine());
        return Task.CompletedTask;
    }

    public Task UnregisterAsync(string scheme, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        string key = Class(scheme);
        bool modShelfs = IsModShelfs(scheme);

        // A registration or a giving back that stopped halfway left a class of ModShelf's without a command, or none.
        bool unfinished = !HasCommand(user, key) && (user.GetString(key, Marker) is not null || !user.Exists(key));
        if ((modShelfs || unfinished) && user.Exists(Previous(scheme)))
        {
            user.DeleteTree(key);
            user.CopyTree(Previous(scheme), key);
        }
        else if (modShelfs || (unfinished && user.Exists(key)))
        {
            user.DeleteTree(key);
        }
        else
        {
            // Another app took the links since: it keeps them, without the value ModShelf left in its class.
            user.DeleteValue(key, Marker);
        }

        user.DeleteTree(Previous(scheme));
        user.DeleteIfEmpty(PreviousRoot);
        user.DeleteIfEmpty(@"Software\ModShelf");
        return Task.CompletedTask;
    }

    private static string Class(string scheme) => $@"Software\Classes\{scheme}";

    private static string Previous(string scheme) => $@"{PreviousRoot}\{scheme}";

    private static string Command(string key) => $@"{key}\shell\open\command";

    private static bool HasCommand(IRegistryTree tree, string key) => WindowsCommandLine.Program(tree.GetString(Command(key), null)) is not null;

    /// <summary>
    /// The name of the app a class opens links with, from its command; null when it has none.
    /// </summary>
    private static string? AppOf(IRegistryTree tree, string key) => WindowsCommandLine.AppName(tree.GetString(Command(key), null));

    /// <summary>
    /// Whether the scheme's class is a ModShelf's: its marker names the program its command starts.
    /// </summary>
    private bool IsModShelfs(string scheme)
        => user.GetString(Class(scheme), Marker) is { } marked
            && string.Equals(WindowsCommandLine.Program(user.GetString(Command(Class(scheme)), null)), marked, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the scheme's class starts this ModShelf, on this library: the command this registration writes.
    /// </summary>
    private bool IsThisModShelfs(string scheme)
        => string.Equals(user.GetString(Command(Class(scheme)), null), CommandLine(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The app put aside for the links, or else the one the whole machine has for them, which opens them again once the
    /// current user's class is gone.
    /// </summary>
    private string? PreviousApp(string scheme)
        => (user.Exists(Previous(scheme)) ? AppOf(user, Previous(scheme)) : null) ?? AppOf(machine, Class(scheme));

    /// <summary>
    /// The app the user chose for the scheme in Windows' "open with" prompt, when it isn't the scheme's own class.
    /// Windows 11 keeps the choice in <c>UserChoiceLatest</c> once it has moved it there, and in <c>UserChoice</c> before.
    /// </summary>
    private string? ChosenApp(string scheme)
    {
        string associations = $@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}";
        string? progId = user.GetString($@"{associations}\UserChoiceLatest\ProgId", "ProgId") ?? user.GetString($@"{associations}\UserChoice", "ProgId");
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
    private string CommandLine()
        => string.Join(' ', [WindowsCommandLine.Quote(options.DesktopApp), .. options.Arguments.Select(WindowsCommandLine.Quote), "\"%1\""]);
}
