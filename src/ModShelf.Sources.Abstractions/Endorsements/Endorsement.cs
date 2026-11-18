namespace ModShelf.Sources.Abstractions.Endorsements;

/// <summary>
/// What the account said of a mod on its site: endorsed it, or chose not to. A mod the account said nothing of has
/// neither.
/// </summary>
public enum Endorsement
{
    /// <summary>The account endorsed the mod, such as liked it.</summary>
    Endorsed = 1,

    /// <summary>The account chose not to endorse the mod, or took its endorsement back.</summary>
    Abstained = 2,
}
