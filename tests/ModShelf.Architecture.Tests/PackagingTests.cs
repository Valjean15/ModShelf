using System.Text.RegularExpressions;

namespace ModShelf.Architecture.Tests;

/// <summary>
/// The installer's rule in ADR 0038: Velopack installs the Windows app into <c>%LocalAppData%\&lt;pack ID&gt;</c>, and its
/// uninstaller deletes that whole folder, so the pack scripts never give the name of ModShelf's own data folder as the
/// pack ID.
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
        string build = Path.Combine(RepositoryRoot(), "build");

        string bash = PackId(File.ReadAllText(Path.Combine(build, "pack.sh")), BashPackId(), "--packId \"$pack_id\"");
        string powershell = PackId(File.ReadAllText(Path.Combine(build, "pack.ps1")), PowerShellPackId(), "'--packId', $packId");

        Assert.Equal(bash, powershell);
        Assert.False(string.Equals(bash, DataFolder, StringComparison.OrdinalIgnoreCase), $"The pack ID {bash} is ModShelf's data folder.");
    }

    private static string PackId(string script, Regex assignment, string use)
    {
        Assert.Contains(use, script, StringComparison.Ordinal);
        return Assert.Single(assignment.Matches(script)).Groups[1].Value;
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "ModShelf.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException($"No ModShelf.slnx above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@"^pack_id=(\S+)\r?$", RegexOptions.Multiline)]
    private static partial Regex BashPackId();

    [GeneratedRegex(@"^\$packId = '([^']+)'\r?$", RegexOptions.Multiline)]
    private static partial Regex PowerShellPackId();
}
