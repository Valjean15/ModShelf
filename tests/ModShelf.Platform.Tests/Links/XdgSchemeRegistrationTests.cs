using ModShelf.Application.Links;
using ModShelf.Platform.Links;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Links;

/// <summary>
/// ModShelf's desktop entry for <c>x-scheme-handler/nxm</c>, made the user's default and given back, in configuration
/// and data folders of the test's own, on any system.
/// </summary>
public sealed class XdgSchemeRegistrationTests : IDisposable
{
    private const string Mime = "x-scheme-handler/nxm";

    private readonly TempFolder _folder = new();
    private readonly XdgDirectories _directories;
    private readonly string _app;

    public XdgSchemeRegistrationTests()
    {
        _directories = new XdgDirectories(
            In("config"),
            In("data"),
            [In("etc", "xdg")],
            [In("usr", "local", "share"), In("usr", "share")],
            ["gnome"]);
        _app = In("opt", "Mod Shelf", "ModShelf.Desktop");
        Directory.CreateDirectory(Path.GetDirectoryName(_app)!);
        File.WriteAllText(_app, "app");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string UserList => Path.Combine(_directories.ConfigHome, "mimeapps.list");

    private string OurEntry => Path.Combine(_directories.DataHome, "applications", "modshelf-nxm.desktop");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task NoDefaultAndNoAppIsNoApp()
    {
        Assert.Equal(SchemeState.None, await Create().GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task NamesTheUsersDefaultByItsEntrysName()
    {
        AddApp(In("usr", "share"), "vortex.desktop", "Vortex");
        WriteList(UserList, "vortex.desktop;");

        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex"), await Create().GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task ADesktopsOwnListWinsAndAMissingEntryIsPassedOver()
    {
        AddApp(_directories.DataHome, "vortex.desktop", "Vortex");
        AddApp(In("usr", "share"), "other.desktop", "Other");
        WriteList(Path.Combine(_directories.ConfigHome, "gnome-mimeapps.list"), "gone.desktop;other.desktop;");
        WriteList(UserList, "vortex.desktop;");

        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Other"), await Create().GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task TheSystemsDefaultCountsWhenTheUserHasNone()
    {
        AddApp(In("usr", "share"), "vortex.desktop", "Vortex");
        WriteList(Path.Combine(In("usr", "share"), "applications", "mimeapps.list"), "vortex.desktop");

        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex"), await Create().GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task WithNoDefaultTheFirstAppThatSaysItOpensThemCounts()
    {
        AddApp(In("usr", "share"), "zeta.desktop", "Zeta", Mime);
        AddApp(In("usr", "share"), "alpha.desktop", "Alpha", "text/plain;" + Mime + ";");
        AddApp(In("usr", "share"), "hidden.desktop", "Hidden", Mime, hidden: true);

        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Alpha"), await Create().GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task WithoutTheDesktopAppItCantRegister()
    {
        File.Delete(_app);
        XdgSchemeRegistration registration = Create();

        Assert.Equal(SchemeState.Unavailable, await registration.GetAsync("nxm", Cancellation));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registration.RegisterAsync("nxm", Cancellation));
        Assert.False(File.Exists(UserList));
    }

    [Fact]
    public async Task RegisteringMakesItsEntryTheUsersDefaultAndPutsVortexAside()
    {
        AddApp(In("usr", "share"), "vortex.desktop", "Vortex");
        WriteList(UserList, "vortex.desktop;", "text/plain=gedit.desktop");
        XdgSchemeRegistration registration = Create("--ModShelf:DatabasePath=/tmp/test/library.db");

        await registration.RegisterAsync("nxm", Cancellation);

        Assert.Equal(
            [
                "[Desktop Entry]",
                "Type=Application",
                "Name=ModShelf",
                "Comment=Opens nxm:// links in ModShelf",
                $"Exec={DesktopEntryExec.ForLink(_app, ["--ModShelf:DatabasePath=/tmp/test/library.db"])}",
                "Terminal=false",
                "NoDisplay=true",
                $"MimeType={Mime};",
                "X-ModShelf-Previous=vortex.desktop;",
            ],
            File.ReadAllText(OurEntry).TrimEnd('\n').Split('\n'));
        Assert.Equal("modshelf-nxm.desktop;", MimeAppsList.Read(UserList, MimeAppsList.DefaultApplications, Mime));
        Assert.Equal("gedit.desktop", MimeAppsList.Read(UserList, MimeAppsList.DefaultApplications, "text/plain"));
        Assert.Equal(new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "Vortex"), await registration.GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task RegisteringAgainKeepsWhatWasPutAsideTheFirstTime()
    {
        AddApp(In("usr", "share"), "vortex.desktop", "Vortex");
        WriteList(UserList, "vortex.desktop;");
        XdgSchemeRegistration registration = Create();

        await registration.RegisterAsync("nxm", Cancellation);
        await registration.RegisterAsync("nxm", Cancellation);

        Assert.Equal("vortex.desktop;", MimeAppsList.Read(OurEntry, "Desktop Entry", "X-ModShelf-Previous"));
        Assert.Equal(new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "Vortex"), await registration.GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task UnregisteringPutsTheUsersDefaultBack()
    {
        AddApp(In("usr", "share"), "vortex.desktop", "Vortex");
        WriteList(UserList, "vortex.desktop;", "text/plain=gedit.desktop");
        string before = File.ReadAllText(UserList);
        XdgSchemeRegistration registration = Create();
        await registration.RegisterAsync("nxm", Cancellation);

        await registration.UnregisterAsync("nxm", Cancellation);

        Assert.Equal(before, File.ReadAllText(UserList));
        Assert.False(File.Exists(OurEntry));
        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex"), await registration.GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task WithNoUserDefaultBeforeTheSystemsOpensThemAgain()
    {
        AddApp(In("usr", "share"), "vortex.desktop", "Vortex");
        WriteList(Path.Combine(In("etc", "xdg"), "mimeapps.list"), "vortex.desktop;");
        XdgSchemeRegistration registration = Create();
        await registration.RegisterAsync("nxm", Cancellation);

        Assert.Equal(new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "Vortex"), await registration.GetAsync("nxm", Cancellation));

        await registration.UnregisterAsync("nxm", Cancellation);

        Assert.Null(MimeAppsList.Read(UserList, MimeAppsList.DefaultApplications, Mime));
        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex"), await registration.GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task WithNoAppBeforeNoneOpensThemOnceGivenBack()
    {
        XdgSchemeRegistration registration = Create();
        await registration.RegisterAsync("nxm", Cancellation);

        Assert.Equal(new SchemeState(SchemeHandlerKind.ModShelf), await registration.GetAsync("nxm", Cancellation));

        await registration.UnregisterAsync("nxm", Cancellation);

        Assert.Equal(SchemeState.None, await registration.GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task AnAppThatTookThemSinceKeepsThem()
    {
        AddApp(In("usr", "share"), "vortex.desktop", "Vortex");
        XdgSchemeRegistration registration = Create();
        await registration.RegisterAsync("nxm", Cancellation);
        MimeAppsList.Write(UserList, MimeAppsList.DefaultApplications, Mime, "vortex.desktop;");

        await registration.UnregisterAsync("nxm", Cancellation);

        Assert.Equal("vortex.desktop;", MimeAppsList.Read(UserList, MimeAppsList.DefaultApplications, Mime));
        Assert.False(File.Exists(OurEntry));
    }

    [Fact]
    public async Task ADesktopsOwnDefaultStillWinsOverTheRegistration()
    {
        AddApp(In("usr", "share"), "vortex.desktop", "Vortex");
        WriteList(Path.Combine(_directories.ConfigHome, "gnome-mimeapps.list"), "vortex.desktop;");
        XdgSchemeRegistration registration = Create();

        await registration.RegisterAsync("nxm", Cancellation);

        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex"), await registration.GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task UnregisteringWhatWasNeverRegisteredChangesNothing()
    {
        WriteList(UserList, "vortex.desktop;");
        string before = File.ReadAllText(UserList);

        await Create().UnregisterAsync("nxm", Cancellation);

        Assert.Equal(before, File.ReadAllText(UserList));
    }

    private string In(params string[] parts) => Path.Combine([_folder.Path, .. parts]);

    private static void AddApp(string dataFolder, string id, string name, string? mime = null, bool hidden = false)
    {
        string folder = Path.Combine(dataFolder, "applications");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(
            Path.Combine(folder, id),
            ["[Desktop Entry]", "Type=Application", $"Name={name}", $"Exec={name.ToLowerInvariant()} %u", .. mime is null ? Array.Empty<string>() : [$"MimeType={mime}"], .. hidden ? ["Hidden=true"] : Array.Empty<string>()]);
    }

    private static void WriteList(string file, string value, params string[] others)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllLines(file, ["[Default Applications]", $"{Mime}={value}", .. others]);
    }

    private XdgSchemeRegistration Create(params string[] arguments) => new(_directories, new SchemeHandlerOptions(_app, arguments));
}
