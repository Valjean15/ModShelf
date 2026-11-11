using System.Globalization;
using System.Text;

using NBitcoin.Secp256k1;

namespace ModShelf.Sources.DegMods.Api;

/// <summary>
/// Whether an event is what its author signed: its ID the SHA-256 of what it says, as NIP-01 serializes it, and its
/// signature the author's Schnorr signature of that ID (BIP-340). A relay can send anything; an event that isn't both is
/// never believed.
/// </summary>
internal static class NostrSignatures
{
    public static bool IsGenuine(NostrEvent nostrEvent)
    {
        ArgumentNullException.ThrowIfNull(nostrEvent);

        byte[] id = ComputeId(nostrEvent);
        return string.Equals(Convert.ToHexStringLower(id), nostrEvent.Id, StringComparison.Ordinal)
            && ECXOnlyPubKey.TryCreate(Convert.FromHexString(nostrEvent.PubKey), out ECXOnlyPubKey? author)
            && SecpSchnorrSignature.TryCreate(Convert.FromHexString(nostrEvent.Sig), out SecpSchnorrSignature? signature)
            && author.SigVerifyBIP340(signature, id);
    }

    /// <summary>
    /// The SHA-256 of <c>[0, pubkey, created_at, kind, tags, content]</c>, written as NIP-01 says: JSON without spaces,
    /// UTF-8, escaping only what JSON must.
    /// </summary>
    public static byte[] ComputeId(NostrEvent nostrEvent)
    {
        ArgumentNullException.ThrowIfNull(nostrEvent);

        StringBuilder text = new("[0,");
        Write(text, nostrEvent.PubKey);
        text.Append(',').Append(nostrEvent.CreatedAt.ToString(CultureInfo.InvariantCulture))
            .Append(',').Append(nostrEvent.Kind.ToString(CultureInfo.InvariantCulture))
            .Append(",[");
        for (int tag = 0; tag < nostrEvent.Tags.Count; tag++)
        {
            text.Append(tag == 0 ? "[" : ",[");
            IReadOnlyList<string> values = nostrEvent.Tags[tag];
            for (int value = 0; value < values.Count; value++)
            {
                if (value > 0)
                {
                    text.Append(',');
                }

                Write(text, values[value]);
            }

            text.Append(']');
        }

        text.Append("],");
        Write(text, nostrEvent.Content);
        text.Append(']');
        return System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>
    /// A JSON string as NIP-01 writes it: a quote, a backslash and the control characters escaped, the short way where
    /// JSON has one; anything else as it is.
    /// </summary>
    private static void Write(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\b':
                    text.Append("\\b");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\f':
                    text.Append("\\f");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case < ' ':
                    text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    text.Append(character);
                    break;
            }
        }

        text.Append('"');
    }
}
