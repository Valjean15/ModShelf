using System.Text.Json;

namespace ModShelf.Sources.DegMods.Api;

/// <summary>
/// A Nostr event (NIP-01) as a relay sent it: who signed it, when, what kind, its tags and content, and the signature.
/// What it says is believed only once <see cref="NostrSignatures.IsGenuine"/> says its author signed it.
/// </summary>
internal sealed record NostrEvent
{
    public NostrEvent(string id, string pubKey, long createdAt, int kind, IReadOnlyList<IReadOnlyList<string>> tags, string content, string sig)
    {
        Id = id;
        PubKey = pubKey;
        CreatedAt = createdAt;
        Kind = kind;
        Tags = tags;
        Content = content;
        Sig = sig;
    }

    /// <summary>
    /// The SHA-256 of what the event says, in lowercase hexadecimal: 64 digits.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The author's public key, x-only, in lowercase hexadecimal: 64 digits.
    /// </summary>
    public string PubKey { get; }

    /// <summary>
    /// When the author says they made it, in Unix seconds.
    /// </summary>
    public long CreatedAt { get; }

    public int Kind { get; }

    /// <summary>
    /// Its tags, each a name and its values.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Tags { get; }

    public string Content { get; }

    /// <summary>
    /// The author's Schnorr signature of <see cref="Id"/>, in lowercase hexadecimal: 128 digits.
    /// </summary>
    public string Sig { get; }

    /// <summary>
    /// The value of an addressable event's <c>d</c> tag, which names it among its author's events of its kind; empty
    /// when it has none.
    /// </summary>
    public string D => Tag("d") ?? string.Empty;

    /// <summary>
    /// The first value of the first tag named <paramref name="name"/>, or null when it has none.
    /// </summary>
    public string? Tag(string name)
        => Tags.FirstOrDefault(tag => tag.Count >= 2 && string.Equals(tag[0], name, StringComparison.Ordinal)) is { } found ? found[1] : null;

    /// <summary>
    /// The values of every tag named <paramref name="name"/>, in order: each tag's values after its name.
    /// </summary>
    public IEnumerable<string> Values(string name)
        => Tags.Where(tag => tag.Count >= 2 && string.Equals(tag[0], name, StringComparison.Ordinal)).SelectMany(tag => tag.Skip(1));

    /// <summary>
    /// The first value of each tag named <paramref name="name"/>, in order.
    /// </summary>
    public IEnumerable<string> FirstValues(string name)
        => Tags.Where(tag => tag.Count >= 2 && string.Equals(tag[0], name, StringComparison.Ordinal)).Select(tag => tag[1]);

    /// <summary>
    /// The event a relay sent, or null when it isn't one as NIP-01 shapes it: hexadecimal of the right lengths, a kind,
    /// a time, tags of strings and content.
    /// </summary>
    public static NostrEvent? Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || Hex(element, "id", 64) is not { } id
            || Hex(element, "pubkey", 64) is not { } pubKey
            || Hex(element, "sig", 128) is not { } sig
            || !element.TryGetProperty("created_at", out JsonElement created) || created.ValueKind != JsonValueKind.Number
            || !created.TryGetInt64(out long createdAt) || createdAt < 0
            || !element.TryGetProperty("kind", out JsonElement kindValue) || kindValue.ValueKind != JsonValueKind.Number
            || !kindValue.TryGetInt32(out int kind) || kind < 0
            || !element.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.String
            || !element.TryGetProperty("tags", out JsonElement tagsValue) || tagsValue.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<IReadOnlyList<string>> tags = [];
        foreach (JsonElement tag in tagsValue.EnumerateArray())
        {
            if (tag.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            List<string> values = [];
            foreach (JsonElement value in tag.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                values.Add(value.GetString()!);
            }

            tags.Add(values);
        }

        return new NostrEvent(id, pubKey, createdAt, kind, tags, content.GetString()!, sig);
    }

    /// <summary>
    /// The property's value when it's lowercase hexadecimal of that many digits, as NIP-01 writes keys, IDs and
    /// signatures.
    /// </summary>
    private static string? Hex(JsonElement element, string name, int length)
        => element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text
            && text.Length == length
            && text.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')
                ? text
                : null;
}
