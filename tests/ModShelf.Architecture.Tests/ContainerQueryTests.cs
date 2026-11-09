using System.Xml.Linq;

namespace ModShelf.Architecture.Tests;

/// <summary>
/// Where the desktop app's views put their container queries (ADR 0084). Avalonia matches a container query against
/// every control within the reach of the styles it's in, and each control then watches the container, so a query in a
/// screen's own styles made a game's hundreds of cards take minutes to come and go. A query sits on what holds the
/// controls it places, never on a screen of lists.
/// </summary>
public sealed class ContainerQueryTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    /// <summary>
    /// The views whose own styles may hold container queries: a page's few controls, never a list's hundreds.
    /// </summary>
    private static readonly string[] FewControls = ["SettingsView.axaml", "ModPageView.axaml"];

    [Fact]
    public void NoScreenOfListsHasAContainerQueryInItsOwnStyles()
    {
        string[] views = [.. Directory.EnumerateFiles(Path.Combine(Repository.Root, "src", "ModShelf.Desktop"), "*.axaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];
        Assert.Contains(views, view => Path.GetFileName(view) == "LibraryView.axaml");

        string[] violations =
        [
            .. views
                .Where(view => !FewControls.Contains(Path.GetFileName(view)))
                .SelectMany(view => XDocument.Load(view).Descendants(Avalonia + "ContainerQuery")
                    .Where(query => query.Parent?.Name.LocalName is "UserControl.Styles" or "Window.Styles" or "Application.Styles" or "Styles")
                    .Select(query => $"{Path.GetFileName(view)}: {query.Attribute("Name")?.Value} {query.Attribute("Query")?.Value}")),
        ];

        Violations.AssertNone("A container query sits on what holds the controls it places", violations);
    }

    [Fact]
    public void EveryViewAllowedThemExists()
    {
        foreach (string view in FewControls)
        {
            Assert.Single(Directory.EnumerateFiles(Path.Combine(Repository.Root, "src", "ModShelf.Desktop"), view, SearchOption.AllDirectories));
        }
    }
}
