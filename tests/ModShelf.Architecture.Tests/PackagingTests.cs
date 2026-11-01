using System.Text.RegularExpressions;

namespace ModShelf.Architecture.Tests;

/// <summary>
/// The installer's rules: Velopack installs the Windows app into <c>%LocalAppData%\&lt;pack ID&gt;</c>, and its
/// uninstaller deletes that whole folder, so the pack scripts never give the name of ModShelf's own data folder as the
/// pack ID (ADR 0038); and the CLI ships beside the desktop app (ADR 0040).
/// </summary>
public sealed partial class PackagingTests
{
    /// <summary>
    /// The folder under the local application data that holds the library database, the settings, the logs and, on
    /// Windows, the secrets: Infrastructure's <c>LibraryDatabaseLocation</c> and Platform's <c>SecretStores</c>.
    /// </summary>
    private const string DataFolder = "ModShelf";

    [Fact]
    public void TheWindowsAppIsNeverInstalledIntoModShelfsDataFolder()
    {
        string build = Path.Combine(Repository.Root, "build");

        string bash = PackId(File.ReadAllText(Path.Combine(build, "pack.sh")), BashPackId(), "--packId \"$pack_id\"");
        string powershell = PackId(File.ReadAllText(Path.Combine(build, "pack.ps1")), PowerShellPackId(), "'--packId', $packId");

        Assert.Equal(bash, powershell);
        Assert.False(string.Equals(bash, DataFolder, StringComparison.OrdinalIgnoreCase), $"The pack ID {bash} is ModShelf's data folder.");
    }

    /// <summary>
    /// ADR 0040: the CLI, and its MCP server, ship in the desktop app's folder, so they update with it. The two share
    /// that folder, so the files only one of them has keep names of their own.
    /// </summary>
    [Fact]
    public void TheCliShipsInTheDesktopAppsFolderWithFilesOfItsOwn()
    {
        string build = Path.Combine(Repository.Root, "build");
        string bash = File.ReadAllText(Path.Combine(build, "pack.sh"));
        string powershell = File.ReadAllText(Path.Combine(build, "pack.ps1"));

        Assert.Contains("cli=\"$root/src/ModShelf.Cli/ModShelf.Cli.csproj\"", bash, StringComparison.Ordinal);
        Assert.Contains("dotnet publish \"$cli\" -nologo -c Release -r \"$runtime\" --self-contained -o \"$publish\"", bash, StringComparison.Ordinal);
        Assert.Contains("$cli = [IO.Path]::Combine($root, 'src', 'ModShelf.Cli', 'ModShelf.Cli.csproj')", powershell, StringComparison.Ordinal);
        Assert.Contains("@('publish', $cli, '-nologo', '-c', 'Release', '-r', $runtime, '--self-contained', '-o', $publish,", powershell, StringComparison.Ordinal);

        string[] desktop = ContentFiles(Path.Combine(Repository.Root, "src", "ModShelf.Desktop", "ModShelf.Desktop.csproj"));
        string[] cli = ContentFiles(Path.Combine(Repository.Root, "src", "ModShelf.Cli", "ModShelf.Cli.csproj"));
        Assert.NotEmpty(cli);
        Assert.Empty(desktop.Intersect(cli, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The files a project copies beside its build, by name.
    /// </summary>
    private static string[] ContentFiles(string project)
        => [.. ContentInclude().Matches(File.ReadAllText(project)).Select(match => Path.GetFileName(match.Groups[1].Value))];

    private static string PackId(string script, Regex assignment, string use)
    {
        Assert.Contains(use, script, StringComparison.Ordinal);
        return Assert.Single(assignment.Matches(script)).Groups[1].Value;
    }

    [GeneratedRegex(@"<Content Include=""([^""]+)""")]
    private static partial Regex ContentInclude();

    [GeneratedRegex(@"^pack_id=(\S+)\r?$", RegexOptions.Multiline)]
    private static partial Regex BashPackId();

    [GeneratedRegex(@"^\$packId = '([^']+)'\r?$", RegexOptions.Multiline)]
    private static partial Regex PowerShellPackId();
}
