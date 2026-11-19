namespace ModShelf.Sources.Thunderstore.Catalog;

/// <summary>
/// A package's name on Thunderstore, which never changes: its team's and its own, written <c>Team-Name</c> as dependency
/// strings and ModShelf's mod IDs write it. A team's name can hold a hyphen, as older ones such as <c>F-tank</c> do, and a
/// package's can't, so the package's name is what follows the last hyphen, as Thunderstore reads it.
/// </summary>
internal sealed record PackageName
{
    // Thunderstore names are short: its database takes 64 characters for a team or a package, 32 for a version.
    private const int MostLength = 64;
    private const int MostVersionLength = 32;

    /// <exception cref="ArgumentException">Either name isn't one Thunderstore gives.</exception>
    public PackageName(string team, string name)
    {
        if (!IsTeam(team) || !IsName(name))
        {
            throw new ArgumentException($"'{team}-{name}' isn't a Thunderstore package's name.", nameof(name));
        }

        Team = team;
        Name = name;
    }

    /// <summary>
    /// The team that uploaded it, as Thunderstore calls the package's owner and namespace.
    /// </summary>
    public string Team { get; }

    public string Name { get; }

    /// <summary>
    /// <c>Team-Name</c>, the mod's ID in ModShelf.
    /// </summary>
    public string FullName => $"{Team}-{Name}";

    /// <summary>
    /// The name as Thunderstore shows it, its underscores as spaces.
    /// </summary>
    public string DisplayName => Name.Replace('_', ' ');

    /// <summary>
    /// The name of <paramref name="fullName"/>, <c>Team-Name</c>; null when it isn't one.
    /// </summary>
    public static PackageName? Parse(string? fullName)
    {
        int dash = fullName?.LastIndexOf('-') ?? -1;
        if (fullName is null || dash <= 0 || dash == fullName.Length - 1)
        {
            return null;
        }

        string team = fullName[..dash];
        string name = fullName[(dash + 1)..];
        return IsTeam(team) && IsName(name) ? new PackageName(team, name) : null;
    }

    /// <summary>
    /// The package and version a dependency string names, such as <c>BepInEx-BepInExPack-5.4.2305</c>; null when it
    /// isn't one.
    /// </summary>
    public static (PackageName Package, string Version)? ParseDependency(string? dependency)
    {
        int dash = dependency?.LastIndexOf('-') ?? -1;
        if (dependency is null || dash <= 0)
        {
            return null;
        }

        string version = dependency[(dash + 1)..];
        return IsVersion(version) && Parse(dependency[..dash]) is { } package ? (package, version) : null;
    }

    /// <summary>
    /// Whether <paramref name="version"/> is a version number as Thunderstore takes them: three numbers, such as
    /// <c>1.4.66</c>.
    /// </summary>
    public static bool IsVersion(string? version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > MostVersionLength)
        {
            return false;
        }

        string[] parts = version.Split('.');
        return parts.Length == 3 && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    public override string ToString() => FullName;

    private static bool IsName(string? name)
        => !string.IsNullOrEmpty(name) && name.Length <= MostLength && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool IsTeam(string? team)
        => !string.IsNullOrEmpty(team)
            && team.Length <= MostLength
            && team[0] != '-'
            && team[^1] != '-'
            && team.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
