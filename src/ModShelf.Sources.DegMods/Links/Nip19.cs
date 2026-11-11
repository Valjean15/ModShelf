using System.Buffers.Binary;
using System.Text;

namespace ModShelf.Sources.DegMods.Links;

/// <summary>
/// Nostr's keys and addresses as text (NIP-19): a public key as <c>npub1…</c>, and an addressable event, such as a mod,
/// as <c>naddr1…</c>, which holds its kind, its author and its <c>d</c> tag, and may hold relays too.
/// </summary>
internal static class Nip19
{
    private const string PublicKeyPrefix = "npub";
    private const string AddressPrefix = "naddr";

    // An naddr's TLV types: the d tag, a relay, the author, the kind.
    private const byte Identifier = 0;
    private const byte Author = 2;
    private const byte Kind = 3;

    public static string Npub(string publicKey) => Bech32.Encode(PublicKeyPrefix, Convert.FromHexString(publicKey));

    /// <summary>
    /// The public key, in lowercase hexadecimal, an <c>npub1…</c> names; false for anything else.
    /// </summary>
    public static bool TryReadNpub(string? text, out string publicKey)
    {
        publicKey = string.Empty;
        if (!Bech32.TryDecode(text, out string prefix, out byte[] data) || prefix != PublicKeyPrefix || data.Length != 32)
        {
            return false;
        }

        publicKey = Convert.ToHexStringLower(data);
        return true;
    }

    /// <summary>
    /// The address of the author's event of that kind with that <c>d</c> tag, without relays, as DEG Mods writes its
    /// mods' links.
    /// </summary>
    public static string Naddr(int kind, string publicKey, string d)
    {
        byte[] identifier = Encoding.UTF8.GetBytes(d);
        byte[] author = Convert.FromHexString(publicKey);
        List<byte> data = [Identifier, checked((byte)identifier.Length), .. identifier, Author, (byte)author.Length, .. author, Kind, 4];
        byte[] kindBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(kindBytes, checked((uint)kind));
        data.AddRange(kindBytes);
        return Bech32.Encode(AddressPrefix, [.. data]);
    }

    /// <summary>
    /// The kind, author and <c>d</c> tag an <c>naddr1…</c> names, its relays left out; false for anything else, or an
    /// address without all three.
    /// </summary>
    public static bool TryReadNaddr(string? text, out (int Kind, string PublicKey, string D) address)
    {
        address = default;
        if (!Bech32.TryDecode(text, out string prefix, out byte[] data) || prefix != AddressPrefix)
        {
            return false;
        }

        string? d = null;
        string? author = null;
        int? kind = null;
        for (int at = 0; at < data.Length;)
        {
            if (at + 2 > data.Length || at + 2 + data[at + 1] > data.Length)
            {
                return false;
            }

            byte type = data[at];
            ReadOnlySpan<byte> value = data.AsSpan(at + 2, data[at + 1]);
            switch (type)
            {
                case Identifier when d is null:
                    d = Encoding.UTF8.GetString(value);
                    break;
                case Author when author is null && value.Length == 32:
                    author = Convert.ToHexStringLower(value);
                    break;
                case Kind when kind is null && value.Length == 4:
                    uint read = BinaryPrimitives.ReadUInt32BigEndian(value);
                    kind = read <= int.MaxValue ? (int)read : null;
                    break;
            }

            at += 2 + data[at + 1];
        }

        if (d is null || author is null || kind is null)
        {
            return false;
        }

        address = (kind.Value, author, d);
        return true;
    }
}
