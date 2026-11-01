namespace ModShelf.Architecture.Tests;

/// <summary>
/// The repository the tests were built from, for the checks of its files other than code, such as the build scripts.
/// </summary>
internal static class Repository
{
    /// <summary>
    /// The repository's root folder: the first folder above the tests that holds <c>ModShelf.slnx</c>.
    /// </summary>
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
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
}
