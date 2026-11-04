using ModShelf.Domain.Mods;

namespace ModShelf.Testing;

/// <summary>
/// A mod of the <see cref="ExampleSite"/>, with its files, oldest first.
/// </summary>
internal sealed class ExampleMod(ModRef mod, string name, string summary, string author, long downloads)
{
    public ModRef Mod { get; } = mod;

    public string Name { get; } = name;

    public string Summary { get; } = summary;

    public string Author { get; } = author;

    public long Downloads { get; } = downloads;

    public List<ModFile> Files { get; } = [];
}
