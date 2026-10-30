using System.Reflection;

namespace ModShelf.Application.Diagnostics;

/// <summary>
/// This ModShelf: its version, as the git tag it was built from gives it, and the commit.
/// </summary>
/// <param name="Version">Such as <c>0.9.0</c>, or <c>0.9.1-alpha.0.3</c> between tags.</param>
/// <param name="Commit">The commit's first seven characters, or null when the build didn't know it.</param>
public sealed record AppInfo(string Version, string? Commit)
{
    private const int CommitLength = 7;

    /// <summary>
    /// The version an assembly was built with, from its informational version, such as <c>0.9.0+a60d76b0…</c>.
    /// </summary>
    public static AppInfo Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }

    /// <summary>
    /// The version and commit in an informational version, such as <c>0.9.0+a60d76b0…</c>; <c>0.0.0</c> without one.
    /// </summary>
    public static AppInfo Parse(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return new AppInfo("0.0.0", null);
        }

        string[] parts = informationalVersion.Trim().Split('+', 2);
        string? commit = parts.Length == 2 && parts[1].Length > 0 ? parts[1][..Math.Min(CommitLength, parts[1].Length)] : null;
        return new AppInfo(parts[0], commit);
    }

    /// <summary>
    /// Such as <c>0.9.0 (a60d76b)</c>.
    /// </summary>
    public override string ToString() => Commit is null ? Version : $"{Version} ({Commit})";
}
