using System.Text;

namespace ModShelf.Platform.Installed;

/// <summary>
/// A key of Steam's text format of keys and values (KeyValues, its <c>.vdf</c> and <c>.acf</c> files), with its text
/// value, or the keys it groups. Keys are found by name in any case, as Steam's files write them in several.
/// </summary>
/// <remarks>
/// Keys and values are quoted, with <c>\\</c>, <c>\"</c>, <c>\n</c> and <c>\t</c> for what they stand for, or unquoted
/// words; <c>{</c> and <c>}</c> group keys; <c>//</c> starts a comment to the end of the line; a condition in brackets
/// after a value, such as <c>[$WIN32]</c>, is passed over.
/// </remarks>
internal sealed class KeyValue
{
    /// <summary>
    /// How deep groups may go: Steam's files go three or four deep.
    /// </summary>
    internal const int MostDepth = 32;

    private KeyValue(string name, string? value, IReadOnlyList<KeyValue> children)
    {
        Name = name;
        Value = value;
        Children = children;
    }

    public string Name { get; }

    /// <summary>
    /// The key's text; null for a group.
    /// </summary>
    public string? Value { get; }

    /// <summary>
    /// The keys a group holds, in order; none for a key with a text value.
    /// </summary>
    public IReadOnlyList<KeyValue> Children { get; }

    /// <summary>
    /// The first key of the group named <paramref name="name"/>, in any case; null for none.
    /// </summary>
    public KeyValue? this[string name] => Children.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The text of the group's first key named <paramref name="name"/>; null when there's none, or it's a group.
    /// </summary>
    public string? Text(string name) => this[name]?.Value;

    /// <summary>
    /// Reads a file's keys, as a group holding its top keys.
    /// </summary>
    /// <exception cref="InvalidDataException">The text isn't Steam's format, or groups go deeper than <see cref="MostDepth"/>.</exception>
    public static KeyValue Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int at = 0;
        return new KeyValue(string.Empty, null, Group(text, ref at, depth: 0, out _));
    }

    /// <summary>
    /// The keys up to a group's closing brace, which <paramref name="closed"/> says was found, or to the text's end.
    /// </summary>
    private static List<KeyValue> Group(string text, ref int at, int depth, out bool closed)
    {
        closed = false;
        List<KeyValue> keys = [];
        while (Token(text, ref at) is { } name)
        {
            if (name.Is('}'))
            {
                closed = depth > 0 ? true : throw new InvalidDataException($"A '}}' at {at - 1} closes no group.");
                return keys;
            }

            if (name.Is('{'))
            {
                throw new InvalidDataException($"A group at {at - 1} has no key.");
            }

            Word value = Token(text, ref at) ?? throw new InvalidDataException($"The key '{name.Text}' has no value.");
            if (value.Is('{'))
            {
                if (depth == MostDepth)
                {
                    throw new InvalidDataException($"Groups go deeper than {MostDepth}.");
                }

                List<KeyValue> children = Group(text, ref at, depth + 1, out bool inner);
                keys.Add(inner ? new KeyValue(name.Text, null, children) : throw new InvalidDataException($"The group '{name.Text}' isn't closed."));
            }
            else
            {
                keys.Add(value.Is('}') ? throw new InvalidDataException($"The key '{name.Text}' has no value.") : new KeyValue(name.Text, value.Text, []));
                SkipCondition(text, ref at);
            }
        }

        return keys;
    }

    /// <summary>
    /// The next word, quoted or not, or brace; null at the text's end.
    /// </summary>
    private static Word? Token(string text, ref int at)
    {
        SkipSpace(text, ref at);
        if (at >= text.Length)
        {
            return null;
        }

        char first = text[at];
        if (first is '{' or '}')
        {
            at++;
            return new Word(first.ToString(), Quoted: false);
        }

        if (first == '"')
        {
            return new Word(Quoted(text, ref at), Quoted: true);
        }

        int start = at;
        while (at < text.Length && !char.IsWhiteSpace(text[at]) && text[at] is not ('{' or '}' or '"'))
        {
            at++;
        }

        return new Word(text[start..at], Quoted: false);
    }

    private static string Quoted(string text, ref int at)
    {
        int start = at;
        at++;
        StringBuilder value = new();
        while (at < text.Length)
        {
            char next = text[at++];
            if (next == '"')
            {
                return value.ToString();
            }

            if (next == '\\' && at < text.Length)
            {
                char escaped = text[at++];
                value.Append(escaped switch
                {
                    'n' => "\n",
                    't' => "\t",
                    '\\' or '"' => escaped.ToString(),
                    _ => $"\\{escaped}",
                });
            }
            else
            {
                value.Append(next);
            }
        }

        throw new InvalidDataException($"The quote at {start} isn't closed.");
    }

    private static void SkipSpace(string text, ref int at)
    {
        while (at < text.Length)
        {
            if (char.IsWhiteSpace(text[at]))
            {
                at++;
            }
            else if (text[at] == '/' && at + 1 < text.Length && text[at + 1] == '/')
            {
                int end = text.IndexOf('\n', at);
                at = end < 0 ? text.Length : end + 1;
            }
            else
            {
                return;
            }
        }
    }

    private static void SkipCondition(string text, ref int at)
    {
        int before = at;
        SkipSpace(text, ref at);
        if (at < text.Length && text[at] == '[')
        {
            int end = text.IndexOf(']', at);
            at = end < 0 ? throw new InvalidDataException($"The condition at {at} isn't closed.") : end + 1;
        }
        else
        {
            at = before;
        }
    }

    private readonly record struct Word(string Text, bool Quoted)
    {
        /// <summary>
        /// Whether it's the brace, unquoted: a quoted brace is a word.
        /// </summary>
        public bool Is(char brace) => !Quoted && Text.Length == 1 && Text[0] == brace;
    }
}
