using System.Globalization;

namespace ModShelf.Application.Common;

/// <summary>
/// The Roman numerals names use for sequels, from II to XXX, and the numbers they stand for, so either finds the other.
/// I, V and X alone are left out: they're as often letters.
/// </summary>
internal static class RomanNumerals
{
    private const int Most = 30;

    private static readonly Dictionary<string, string> Numbers = Build();

    /// <summary>
    /// A name's word as it may also be written: a numeral's number, such as "12" for "xii", or a number's numeral; none
    /// for any other word.
    /// </summary>
    /// <param name="word">A word as <see cref="NameWords"/> gives it: lowercase.</param>
    public static string? OtherForm(string word) => Numbers.GetValueOrDefault(word);

    /// <summary>
    /// The number a numeral stands for, such as "12" for "xii"; the word itself otherwise.
    /// </summary>
    public static string AsNumber(string word) => Numbers.TryGetValue(word, out string? other) && char.IsAsciiLetter(word[0]) ? other : word;

    private static Dictionary<string, string> Build()
    {
        Dictionary<string, string> forms = new(StringComparer.Ordinal);
        for (int number = 2; number <= Most; number++)
        {
            string numeral = Numeral(number);
            if (numeral.Length > 1)
            {
                string digits = number.ToString(CultureInfo.InvariantCulture);
                forms[numeral] = digits;
                forms[digits] = numeral;
            }
        }

        return forms;
    }

    private static string Numeral(int number)
        => new string('x', number / 10) + (number % 10) switch
        {
            1 => "i",
            2 => "ii",
            3 => "iii",
            4 => "iv",
            5 => "v",
            6 => "vi",
            7 => "vii",
            8 => "viii",
            9 => "ix",
            _ => string.Empty,
        };
}
