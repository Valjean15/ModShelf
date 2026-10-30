using System.Globalization;
using System.Runtime.Versioning;

using Microsoft.Win32;

using ModShelf.Application.Links;
using ModShelf.Platform.Links;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Links;

/// <summary>
/// The real registry, under a key of the test's own in the current user's hive, so the user's own classes are never
/// touched.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryTreeTests : IDisposable
{
    private readonly string _root = $@"Software\ModShelf.Tests\{Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)}";

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
            using RegistryKey? tests = Registry.CurrentUser.OpenSubKey(@"Software\ModShelf.Tests");
            if (tests is { SubKeyCount: 0, ValueCount: 0 })
            {
                Registry.CurrentUser.DeleteSubKey(@"Software\ModShelf.Tests", throwOnMissingSubKey: false);
            }
        }
    }

    [Fact]
    public void SetsAndReadsStringsCreatingTheKeys()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The registry exists only on Windows.");
        WindowsRegistryTree tree = Tree();

        tree.SetString(@"A\B", null, "default");
        tree.SetString(@"A\B", "Named", "value");

        Assert.True(tree.Exists("A"));
        Assert.Equal("default", tree.GetString(@"A\B", null));
        Assert.Equal("value", tree.GetString(@"a\b", "Named"));
        Assert.Null(tree.GetString(@"A\B", "Missing"));
        Assert.Null(tree.GetString(@"A\Missing", null));
    }

    [Fact]
    public void CopiesATreeWithItsValuesOfEveryKind()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The registry exists only on Windows.");
        WindowsRegistryTree tree = Tree();
        tree.SetString("From", null, "URL:NXM Protocol");
        tree.SetString(@"From\shell\open\command", null, "\"C:\\Vortex.exe\" -d \"%1\"");
        using (RegistryKey from = Registry.CurrentUser.OpenSubKey($@"{_root}\From", writable: true)!)
        {
            from.SetValue("EditFlags", 2, RegistryValueKind.DWord);
            from.SetValue("Path", "%ProgramFiles%\\Vortex", RegistryValueKind.ExpandString);
        }

        tree.CopyTree("From", @"Aside\To");

        using RegistryKey to = Registry.CurrentUser.OpenSubKey($@"{_root}\Aside\To")!;
        Assert.Equal("URL:NXM Protocol", to.GetValue(string.Empty));
        Assert.Equal((2, RegistryValueKind.DWord), (to.GetValue("EditFlags"), to.GetValueKind("EditFlags")));
        Assert.Equal(("%ProgramFiles%\\Vortex", RegistryValueKind.ExpandString), (to.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames), to.GetValueKind("Path")));
        Assert.Equal("\"C:\\Vortex.exe\" -d \"%1\"", tree.GetString(@"Aside\To\shell\open\command", null));
    }

    [Fact]
    public void DeletesATreeAndAnEmptyKey()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The registry exists only on Windows.");
        WindowsRegistryTree tree = Tree();
        tree.SetString(@"Gone\Deep", "x", "y");
        tree.SetString(@"Kept\Child", "x", "y");
        tree.SetString(@"Empty\Child", "x", "y");
        tree.DeleteTree(@"Empty\Child");

        tree.DeleteTree("Gone");
        tree.DeleteTree("Missing");
        tree.DeleteIfEmpty("Kept");
        tree.DeleteIfEmpty("Empty");
        tree.DeleteIfEmpty("Missing");

        Assert.False(tree.Exists("Gone"));
        Assert.True(tree.Exists("Kept"));
        Assert.False(tree.Exists("Empty"));
    }

    [Fact]
    public async Task RegistersAndGivesBackInTheRealRegistry()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The registry exists only on Windows.");
        WindowsRegistryTree tree = Tree();
        tree.SetString(@"Software\Classes\modshelf-test\shell\open\command", null, "\"C:\\Other\\Other.exe\" \"%1\"");
        using TempFolder folder = new();
        string app = Path.Combine(folder.Path, "ModShelf.Desktop.exe");
        await File.WriteAllTextAsync(app, "app", TestContext.Current.CancellationToken);
        WindowsSchemeRegistration registration = new(tree, Tree(@"\Machine"), new SchemeHandlerOptions(app, []));

        await registration.RegisterAsync("modshelf-test", TestContext.Current.CancellationToken);
        SchemeState taken = await registration.GetAsync("modshelf-test", TestContext.Current.CancellationToken);
        await registration.UnregisterAsync("modshelf-test", TestContext.Current.CancellationToken);

        Assert.Equal(new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "Other"), taken);
        Assert.Equal("\"C:\\Other\\Other.exe\" \"%1\"", tree.GetString(@"Software\Classes\modshelf-test\shell\open\command", null));
        Assert.False(tree.Exists(@"Software\ModShelf"));
    }

    private WindowsRegistryTree Tree(string under = "")
        => new(Registry.CurrentUser.CreateSubKey(_root + under, writable: true));
}
