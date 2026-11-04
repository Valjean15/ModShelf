namespace ModShelf.Application.Common;

/// <summary>
/// How well a name matches what was typed (<see cref="NameQuery"/>), the best last.
/// </summary>
public enum NameMatch
{
    /// <summary>
    /// A word typed starts no word of the name.
    /// </summary>
    None = 0,

    /// <summary>
    /// Each word typed starts a word of the name, in any order.
    /// </summary>
    Words = 1,

    /// <summary>
    /// The name starts with what was typed, as well as having each word.
    /// </summary>
    Start = 2,
}
