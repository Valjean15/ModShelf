namespace ModShelf.Sources.DegMods.Links;

/// <summary>
/// Bech32 (BIP-173), the text Nostr writes keys and addresses in (NIP-19): a prefix, <c>1</c>, the data five bits to a
/// character, and a six-character checksum. Nostr's can be longer than BIP-173's 90 characters.
/// </summary>
internal static class Bech32
{
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

    /// <summary>
    /// The longest text read: an address holds a mod's name of its own, and nothing longer is ever needed.
    /// </summary>
    private const int MaxLength = 5000;

    private static readonly uint[] Generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];

    public static string Encode(string prefix, ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        byte[] values = ConvertBits(data, 8, 5, pad: true)!;
        byte[] checksum = Checksum(prefix, values);
        char[] text = new char[prefix.Length + 1 + values.Length + checksum.Length];
        prefix.CopyTo(0, text, 0, prefix.Length);
        text[prefix.Length] = '1';
        int at = prefix.Length + 1;
        foreach (byte value in values.Concat(checksum))
        {
            text[at++] = Charset[value];
        }

        return new string(text);
    }

    /// <summary>
    /// The prefix and data of Bech32 text, or false when it isn't Bech32: a character it doesn't use, upper and lower
    /// case mixed, no prefix, or a checksum that doesn't match.
    /// </summary>
    public static bool TryDecode(string? text, out string prefix, out byte[] data)
    {
        prefix = string.Empty;
        data = [];
        if (text is null || text.Length < 8 || text.Length > MaxLength
            || (text.Any(char.IsAsciiLetterUpper) && text.Any(char.IsAsciiLetterLower)))
        {
            return false;
        }

        string lower = text.ToLowerInvariant();
        int separator = lower.LastIndexOf('1');
        if (separator < 1 || separator + 7 > lower.Length || lower[..separator].Any(character => character is < '!' or > '~'))
        {
            return false;
        }

        byte[] values = new byte[lower.Length - separator - 1];
        for (int index = 0; index < values.Length; index++)
        {
            int value = Charset.IndexOf(lower[separator + 1 + index], StringComparison.Ordinal);
            if (value < 0)
            {
                return false;
            }

            values[index] = (byte)value;
        }

        string found = lower[..separator];
        if (Polymod([.. ExpandPrefix(found), .. values]) != 1)
        {
            return false;
        }

        if (ConvertBits(values.AsSpan(0, values.Length - 6), 5, 8, pad: false) is not { } bytes)
        {
            return false;
        }

        prefix = found;
        data = bytes;
        return true;
    }

    private static byte[] Checksum(string prefix, byte[] values)
    {
        uint polymod = Polymod([.. ExpandPrefix(prefix), .. values, 0, 0, 0, 0, 0, 0]) ^ 1;
        byte[] checksum = new byte[6];
        for (int index = 0; index < 6; index++)
        {
            checksum[index] = (byte)((polymod >> (5 * (5 - index))) & 31);
        }

        return checksum;
    }

    private static byte[] ExpandPrefix(string prefix)
        => [.. prefix.Select(character => (byte)(character >> 5)), 0, .. prefix.Select(character => (byte)(character & 31))];

    private static uint Polymod(IEnumerable<byte> values)
    {
        uint check = 1;
        foreach (byte value in values)
        {
            uint top = check >> 25;
            check = ((check & 0x1ffffff) << 5) ^ value;
            for (int bit = 0; bit < 5; bit++)
            {
                if (((top >> bit) & 1) != 0)
                {
                    check ^= Generator[bit];
                }
            }
        }

        return check;
    }

    /// <summary>
    /// The data regrouped from <paramref name="from"/> bits a value to <paramref name="to"/>; null when what's left over
    /// on the way back isn't padding.
    /// </summary>
    private static byte[]? ConvertBits(ReadOnlySpan<byte> data, int from, int to, bool pad)
    {
        int accumulator = 0;
        int bits = 0;
        int most = (1 << to) - 1;
        List<byte> result = [];
        foreach (byte value in data)
        {
            // Only the low bits are ever read, so the high ones may fall off.
            accumulator = (accumulator << from) | value;
            bits += from;
            while (bits >= to)
            {
                bits -= to;
                result.Add((byte)((accumulator >> bits) & most));
            }
        }

        if (pad)
        {
            if (bits > 0)
            {
                result.Add((byte)((accumulator << (to - bits)) & most));
            }
        }
        else if (bits >= from || ((accumulator << (to - bits)) & most) != 0)
        {
            return null;
        }

        return [.. result];
    }
}
