using ModShelf.Sources.Thunderstore.Catalog;

namespace ModShelf.Sources.Thunderstore.Tests.Catalog;

/// <summary>
/// A package's name, <c>Team-Name</c>, read as Thunderstore reads it: the name after the last hyphen.
/// </summary>
public sealed class PackageNameTests
{
    [Theory]
    [InlineData("BepInEx-BepInExPack", "BepInEx", "BepInExPack")]
    [InlineData("Jowday-DamageMeter", "Jowday", "DamageMeter")]
    [InlineData("F-tank-ReapownedModPack", "F-tank", "ReapownedModPack")]
    [InlineData("just_frostice482-Frost_Utils", "just_frostice482", "Frost_Utils")]
    public void ReadsTheNameAfterTheLastHyphen(string fullName, string team, string name)
    {
        PackageName parsed = PackageName.Parse(fullName)!;

        Assert.Equal((team, name, fullName), (parsed.Team, parsed.Name, parsed.FullName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NoHyphen")]
    [InlineData("-Name")]
    [InlineData("Team-")]
    [InlineData("Team-Na me")]
    [InlineData("Te am-Name")]
    [InlineData("Team/x-Name")]
    public void RefusesWhatIsntAName(string? fullName)
    {
        Assert.Null(PackageName.Parse(fullName));
    }

    [Fact]
    public void ShowsUnderscoresAsSpaces()
    {
        Assert.Equal("Flip the Arcana Mod", PackageName.Parse("ReadEmAndWeep-Flip_the_Arcana_Mod")!.DisplayName);
    }

    [Theory]
    [InlineData("BepInEx-BepInExPack-5.4.2305", "BepInEx-BepInExPack", "5.4.2305")]
    [InlineData("F-tank-ReapownedModPack-1.0.0", "F-tank-ReapownedModPack", "1.0.0")]
    public void ReadsADependencyString(string dependency, string fullName, string version)
    {
        (PackageName package, string read) = PackageName.ParseDependency(dependency)!.Value;

        Assert.Equal((fullName, version), (package.FullName, read));
    }

    [Theory]
    [InlineData("BepInEx-BepInExPack")]
    [InlineData("BepInEx-BepInExPack-5.4")]
    [InlineData("BepInExPack-5.4.2305")]
    [InlineData("")]
    public void RefusesWhatIsntADependencyString(string dependency)
    {
        Assert.Null(PackageName.ParseDependency(dependency));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("10.20.300", true)]
    [InlineData("1.0", false)]
    [InlineData("1.0.0.0", false)]
    [InlineData("1.0.0-beta", false)]
    [InlineData("v1.0.0", false)]
    [InlineData("1..0", false)]
    public void TakesThreeNumbersAsAVersion(string version, bool taken)
    {
        Assert.Equal(taken, PackageName.IsVersion(version));
    }
}
