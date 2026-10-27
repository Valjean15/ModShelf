using System.Globalization;

namespace ModShelf.Domain.Library;

/// <summary>
/// What happens to a library file's old versions when a newer version is added: they're kept, moved to an
/// <c>_archive</c> folder, or deleted beyond the newest few. A file's old versions are the files its site says it
/// replaced (see <see cref="Mods.ModFile.ReplacedBy"/>).
/// </summary>
public sealed record OldVersionRule
{
    /// <summary>
    /// The most versions <see cref="KeepLatest"/> takes.
    /// </summary>
    public const int MaxCount = 99;

    private OldVersionRule(OldVersionAction action, int count)
    {
        Action = action;
        Count = count;
    }

    /// <summary>
    /// Keeps every version.
    /// </summary>
    public static OldVersionRule KeepAll { get; } = new(OldVersionAction.KeepAll, 0);

    /// <summary>
    /// Moves old versions into an <c>_archive</c> folder in their folder.
    /// </summary>
    public static OldVersionRule Archive { get; } = new(OldVersionAction.Archive, 0);

    /// <summary>
    /// The rule when none is set: the newest version and the one before it stay.
    /// </summary>
    public static OldVersionRule Default { get; } = KeepLatest(2);

    public OldVersionAction Action { get; }

    /// <summary>
    /// For <see cref="OldVersionAction.KeepLatest"/>, how many versions stay, counting the newest; otherwise 0.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Keeps the newest <paramref name="count"/> versions, counting the newest, and deletes the others.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> isn't from 1 to <see cref="MaxCount"/>.</exception>
    public static OldVersionRule KeepLatest(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxCount);

        return new OldVersionRule(OldVersionAction.KeepLatest, count);
    }

    /// <summary>
    /// Reads a rule as <see cref="ToString"/> writes it: <c>all</c>, <c>archive</c>, or how many versions to keep, such as
    /// <c>2</c>. Case and surrounding spaces don't matter.
    /// </summary>
    /// <exception cref="ArgumentException">The text isn't a rule. The message says what is.</exception>
    public static OldVersionRule Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string rule = text.Trim();
        if (rule.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return KeepAll;
        }

        if (rule.Equals("archive", StringComparison.OrdinalIgnoreCase))
        {
            return Archive;
        }

        if (int.TryParse(rule, NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count is >= 1 and <= MaxCount)
        {
            return KeepLatest(count);
        }

        throw new ArgumentException(
            $"'{text}' isn't a rule for old versions. Use all, archive, or how many versions to keep, from 1 to {MaxCount}, such as 2.",
            nameof(text));
    }

    /// <summary>
    /// <c>all</c>, <c>archive</c>, or how many versions stay, such as <c>2</c>.
    /// </summary>
    public override string ToString() => Action switch
    {
        OldVersionAction.KeepAll => "all",
        OldVersionAction.Archive => "archive",
        _ => Count.ToString(CultureInfo.InvariantCulture),
    };
}
