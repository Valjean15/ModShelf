namespace ModShelf.Sources.Abstractions.Catalog;

/// <summary>
/// What changed in one version of a mod, as its author listed it.
/// </summary>
public sealed record ModChangelog
{
    public ModChangelog(string version, IEnumerable<string> changes)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(changes);

        Version = version.Trim();
        Changes = [.. changes.Where(change => !string.IsNullOrWhiteSpace(change)).Select(change => change.Trim())];
    }

    /// <summary>
    /// The version as the author wrote it. A label, never compared.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// One line per change, as the author wrote them.
    /// </summary>
    public IReadOnlyList<string> Changes { get; }
}
