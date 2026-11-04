namespace ModShelf.Application.Common;

/// <summary>
/// What the user typed to find a game or a mod by its name, as every list that finds names reads it. Each word typed
/// must start a word of the name, in any order; case, accents, apostrophes, quotes, other punctuation and spaces don't
/// count, so a word typed may run on across the name's words ("stalker" finds S.T.A.L.K.E.R., "spiderman" Spider-Man);
/// and a Roman numeral from II to XXX is its number too ("final fantasy 12" finds Final Fantasy XII).
/// </summary>
public sealed record NameQuery
{
    private readonly string[] _words;
    private readonly string _joined;

    private NameQuery(string text, string[] words)
    {
        Text = text;
        _words = words;
        _joined = string.Concat(words);
    }

    /// <summary>
    /// Nothing typed: every name matches, as starting with it.
    /// </summary>
    public static NameQuery Every { get; } = new(string.Empty, []);

    /// <summary>
    /// What was typed, trimmed.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Whether nothing that counts was typed, such as only spaces or punctuation, so every name matches.
    /// </summary>
    public bool IsEmpty => _words.Length == 0;

    public static NameQuery Parse(string? text)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? Every : new NameQuery(trimmed, NameWords.Of(trimmed));
    }

    /// <summary>
    /// Whether two names are the same name, as the same game on two sites: the same once case, accents, apostrophes,
    /// quotes, other punctuation and spaces are left out, and Roman numerals read as numbers.
    /// </summary>
    public static bool SameName(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        string firstKey = Key(first);
        string secondKey = Key(second);
        return firstKey.Length == 0 || secondKey.Length == 0
            ? string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase)
            : string.Equals(firstKey, secondKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// How well <paramref name="name"/> matches.
    /// </summary>
    public NameMatch Match(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (IsEmpty)
        {
            return NameMatch.Start;
        }

        string[][] words = [.. NameWords.Of(name).Select(word => RomanNumerals.OtherForm(word) is { } other ? new[] { word, other } : [word])];
        if (!_words.All(typed => Enumerable.Range(0, words.Length).Any(start => RunsOn(words, start, typed))))
        {
            return NameMatch.None;
        }

        return RunsOn(words, 0, _joined) ? NameMatch.Start : NameMatch.Words;
    }

    /// <summary>
    /// Whether <paramref name="name"/> matches at all.
    /// </summary>
    public bool Matches(string name) => Match(name) != NameMatch.None;

    /// <summary>
    /// The items whose names match, those that start with what was typed first, each kept in its order otherwise: for a
    /// list to pick from. A list in an order the user chose keeps it, and only asks <see cref="Matches"/>.
    /// </summary>
    public IReadOnlyList<T> Find<T>(IEnumerable<T> items, Func<T, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(nameOf);

        return
        [
            .. items
                .Select(item => (Item: item, Match: Match(nameOf(item))))
                .Where(found => found.Match != NameMatch.None)
                .OrderByDescending(found => found.Match)
                .Select(found => found.Item),
        ];
    }

    /// <summary>
    /// Whether both find the same names: the same words typed, whatever the case, accents and punctuation.
    /// </summary>
    public bool Equals(NameQuery? other) => other is not null && _words.SequenceEqual(other._words, StringComparer.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_joined);

    public override string ToString() => Text;

    /// <summary>
    /// The name as two names are compared: its words run together, Roman numerals as numbers.
    /// </summary>
    private static string Key(string name) => string.Concat(NameWords.Of(name).Select(RomanNumerals.AsNumber));

    /// <summary>
    /// Whether the name, from its word at <paramref name="start"/> on, starts with <paramref name="typed"/>, its words
    /// run together, each in either of its forms.
    /// </summary>
    private static bool RunsOn(string[][] words, int start, ReadOnlySpan<char> typed)
    {
        if (typed.IsEmpty)
        {
            return true;
        }

        if (start >= words.Length)
        {
            return false;
        }

        foreach (string form in words[start])
        {
            if (form.AsSpan().StartsWith(typed, StringComparison.Ordinal)
                || (typed.StartsWith(form, StringComparison.Ordinal) && RunsOn(words, start + 1, typed[form.Length..])))
            {
                return true;
            }
        }

        return false;
    }
}
