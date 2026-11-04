using System.Text;

namespace ModShelf.Application.Common;

/// <summary>
/// The words of a text as names are compared: lowercase, without accents, apostrophes or quotes, split wherever there's
/// anything else that's no letter or digit. "Dragon's Dogma 2" is dragons, dogma and 2; "S.T.A.L.K.E.R." is s, t, a, l,
/// k, e and r; "Pokémon" is pokemon.
/// </summary>
internal static class NameWords
{
    // Left out without ending a word, so "Dragon's" is "dragons" whichever apostrophe it's written with.
    private static readonly HashSet<int> Quotes =
    [
        '\'', '"', '`', '\u00B4', '\u2018', '\u2019', '\u201A', '\u201B', '\u201C', '\u201D', '\u201E', '\u2032', '\u2033', '\u02BC',
    ];

    // Letters that carry no accent to drop, but are written otherwise in plain Latin letters.
    private static readonly Dictionary<int, string> Plain = new()
    {
        ['ß'] = "ss",
        ['æ'] = "ae",
        ['ø'] = "o",
        ['œ'] = "oe",
        ['ł'] = "l",
        ['đ'] = "d",
        ['ı'] = "i",
    };

    public static string[] Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> words = [];
        StringBuilder word = new();
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Quotes.Contains(rune.Value))
            {
                continue;
            }

            // Compatibility forms too, so "ﬁ" is fi and "²" is 2.
            foreach (Rune part in rune.ToString().Normalize(NormalizationForm.FormKD).EnumerateRunes())
            {
                switch (Rune.GetUnicodeCategory(part))
                {
                    case System.Globalization.UnicodeCategory.NonSpacingMark:
                    case System.Globalization.UnicodeCategory.SpacingCombiningMark:
                    case System.Globalization.UnicodeCategory.EnclosingMark:
                        continue;
                }

                if (Rune.IsLetterOrDigit(part))
                {
                    Rune lower = Rune.ToLowerInvariant(part);
                    word.Append(Plain.TryGetValue(lower.Value, out string? plain) ? plain : lower.ToString());
                }
                else if (word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }
            }
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        return [.. words];
    }
}
