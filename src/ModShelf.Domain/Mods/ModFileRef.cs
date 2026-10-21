namespace ModShelf.Domain.Mods;

/// <summary>
/// One file of a mod. <see cref="Id"/> is the source's own ID for the file.
/// </summary>
public sealed record ModFileRef
{
    public ModFileRef(ModRef mod, string id)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Mod = mod;
        Id = id;
    }

    public ModRef Mod { get; }

    public string Id { get; }
}
