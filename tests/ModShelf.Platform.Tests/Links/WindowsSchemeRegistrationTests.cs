using ModShelf.Application.Links;
using ModShelf.Platform.Links;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Links;

/// <summary>
/// The <c>nxm</c> class under the current user's <c>Software\Classes</c>: taken from Vortex and given back to it as it
/// was, in registry hives of the test's own, on any system.
/// </summary>
public sealed class WindowsSchemeRegistrationTests : IDisposable
{
    private const string Class = @"Software\Classes\nxm";
    private const string Vortex = @"C:\Program Files\Black Tree Gaming Ltd\Vortex\Vortex.exe";

    private readonly TempFolder _folder = new();
    private readonly MemoryRegistryTree _user = new();
    private readonly MemoryRegistryTree _machine = new();
    private readonly string _app;

    public WindowsSchemeRegistrationTests()
    {
        _app = Path.Combine(_folder.Path, "Mod Shelf", "ModShelf.Desktop.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_app)!);
        File.WriteAllText(_app, "app");
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task NoClassIsNoApp()
    {
        Assert.Equal(SchemeState.None, await Create().GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task NamesTheAppTheUsersClassOpensLinksWith()
    {
        AddVortex(_user);

        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex"), await Create().GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task NamesTheAppTheMachinesClassOpensLinksWith()
    {
        _machine.SetString($@"{Class}\shell\open\command", null, @"C:\MO2\nxmhandler.exe ""%1""");

        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "nxmhandler"), await Create().GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task WithoutTheDesktopAppItCantRegister()
    {
        AddVortex(_user);
        File.Delete(_app);
        WindowsSchemeRegistration registration = Create();

        Assert.Equal(SchemeState.Unavailable, await registration.GetAsync("nxm", Cancellation));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registration.RegisterAsync("nxm", Cancellation));
        Assert.Equal(Vortex, VortexProgram());
    }

    [Fact]
    public async Task RegisteringWritesTheClassAndPutsVortexAside()
    {
        AddVortex(_user);
        WindowsSchemeRegistration registration = Create("--ModShelf:DatabasePath=C:\\Test\\library.db");

        await registration.RegisterAsync("nxm", Cancellation);

        Assert.Equal("URL:nxm Protocol", _user.GetString(Class, null));
        Assert.Equal(string.Empty, _user.GetString(Class, "URL Protocol"));
        Assert.Equal(_app, _user.GetString(Class, "ModShelf"));
        Assert.Equal($"\"{_app}\",0", _user.GetString($@"{Class}\DefaultIcon", null));
        Assert.Equal(
            $"\"{_app}\" \"--ModShelf:DatabasePath=C:\\Test\\library.db\" \"%1\"",
            _user.GetString($@"{Class}\shell\open\command", null));
        Assert.False(_user.Exists($@"{Class}\shell\open\ddeexec"));
        Assert.Equal($"\"{Vortex}\" -d \"%1\"", _user.GetString(@"Software\ModShelf\Previous\nxm\shell\open\command", null));
        Assert.Equal(new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "Vortex"), await registration.GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task RegisteringAgainKeepsWhatWasPutAsideTheFirstTime()
    {
        AddVortex(_user);
        WindowsSchemeRegistration registration = Create();

        await registration.RegisterAsync("nxm", Cancellation);
        await registration.RegisterAsync("nxm", Cancellation);

        Assert.Equal(new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "Vortex"), await registration.GetAsync("nxm", Cancellation));
        Assert.Equal($"\"{Vortex}\" -d \"%1\"", _user.GetString(@"Software\ModShelf\Previous\nxm\shell\open\command", null));
    }

    [Fact]
    public async Task UnregisteringPutsVortexBackAsItWas()
    {
        AddVortex(_user);
        Dictionary<string, Dictionary<string, object>> before = Snapshot(_user);
        WindowsSchemeRegistration registration = Create();
        await registration.RegisterAsync("nxm", Cancellation);

        await registration.UnregisterAsync("nxm", Cancellation);

        Assert.Equal(before, Snapshot(_user));
        Assert.False(_user.Exists(@"Software\ModShelf"));
        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex"), await registration.GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task WithNoClassBeforeTheMachinesAppOpensThemAgain()
    {
        _machine.SetString($@"{Class}\shell\open\command", null, @"""C:\MO2\nxmhandler.exe"" ""%1""");
        WindowsSchemeRegistration registration = Create();
        await registration.RegisterAsync("nxm", Cancellation);

        Assert.Equal(new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "nxmhandler"), await registration.GetAsync("nxm", Cancellation));
        Assert.False(_user.Exists(@"Software\ModShelf\Previous\nxm"));

        await registration.UnregisterAsync("nxm", Cancellation);

        Assert.False(_user.Exists(Class));
        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "nxmhandler"), await registration.GetAsync("nxm", Cancellation));
    }

    [Fact]
    public async Task AnAppThatTookThemSinceKeepsThem()
    {
        WindowsSchemeRegistration registration = Create();
        await registration.RegisterAsync("nxm", Cancellation);
        _user.DeleteTree(Class);
        AddVortex(_user);

        await registration.UnregisterAsync("nxm", Cancellation);

        Assert.Equal(Vortex, VortexProgram());
        Assert.False(_user.Exists(@"Software\ModShelf"));
    }

    [Fact]
    public async Task UnregisteringWhatWasNeverRegisteredChangesNothing()
    {
        AddVortex(_user);
        Dictionary<string, Dictionary<string, object>> before = Snapshot(_user);

        await Create().UnregisterAsync("nxm", Cancellation);

        Assert.Equal(before, Snapshot(_user));
    }

    [Fact]
    public async Task AnAppChosenInWindowsOwnPromptWins()
    {
        _user.SetString(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\nxm\UserChoice", "ProgId", "Vortex.NXM");
        _user.SetString(@"Software\Classes\Vortex.NXM\Application", "ApplicationName", "Vortex Mod Manager");
        WindowsSchemeRegistration registration = Create();

        await registration.RegisterAsync("nxm", Cancellation);

        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Vortex Mod Manager"), await registration.GetAsync("nxm", Cancellation));
    }

    [Theory]
    [InlineData("nxm")]
    [InlineData("NXM")]
    public async Task ChoosingTheSchemesOwnClassInWindowsPromptIsChoosingModShelf(string progId)
    {
        _user.SetString(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\nxm\UserChoice", "ProgId", progId);
        WindowsSchemeRegistration registration = Create();

        await registration.RegisterAsync("nxm", Cancellation);

        Assert.Equal(SchemeHandlerKind.ModShelf, (await registration.GetAsync("nxm", Cancellation)).Kind);
    }

    [Fact]
    public async Task AChosenAppWithoutANameIsNamedByItsCommandOrItsClass()
    {
        _user.SetString(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\nxm\UserChoice", "ProgId", "Other.NXM");
        _user.SetString(@"Software\Classes\Other.NXM\Application", "ApplicationName", "@C:\\Other\\other.dll,-101");

        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Other.NXM"), await Create().GetAsync("nxm", Cancellation));

        _user.SetString(@"Software\Classes\Other.NXM\shell\open\command", null, @"""C:\Other\Other Manager.exe"" ""%1""");

        Assert.Equal(new SchemeState(SchemeHandlerKind.OtherApp, "Other Manager"), await Create().GetAsync("nxm", Cancellation));
    }

    private static void AddVortex(MemoryRegistryTree tree)
    {
        tree.SetString(Class, null, "URL:NXM Protocol");
        tree.SetString(Class, "URL Protocol", string.Empty);
        tree.Set(Class, "EditFlags", 2);
        tree.SetString($@"{Class}\DefaultIcon", null, $"{Vortex},0");
        tree.SetString($@"{Class}\shell\open\command", null, $"\"{Vortex}\" -d \"%1\"");
        tree.SetString($@"{Class}\shell\open\ddeexec", null, string.Empty);
    }

    private static Dictionary<string, Dictionary<string, object>> Snapshot(MemoryRegistryTree tree)
        => tree.Keys.Where(key => key.StartsWith(@"Software\Classes", StringComparison.Ordinal))
            .ToDictionary(key => key, key => tree.ValuesOf(key).ToDictionary(), StringComparer.Ordinal);

    private string? VortexProgram() => WindowsCommandLine.Program(_user.GetString($@"{Class}\shell\open\command", null));

    private WindowsSchemeRegistration Create(params string[] arguments) => new(_user, _machine, new SchemeHandlerOptions(_app, arguments));
}
