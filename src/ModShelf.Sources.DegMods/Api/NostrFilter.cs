using System.Text.Json;

namespace ModShelf.Sources.DegMods.Api;

/// <summary>
/// What a REQ asks a relay for (NIP-01): events of these kinds, by these authors, with these tags, made within a time,
/// at most so many, the newest first.
/// </summary>
internal sealed record NostrFilter
{
    public IReadOnlyList<int>? Kinds { get; init; }

    /// <summary>
    /// The authors' public keys, in lowercase hexadecimal.
    /// </summary>
    public IReadOnlyList<string>? Authors { get; init; }

    /// <summary>
    /// The values of single-letter tags an event must have one of, by the tag's letter, such as <c>d</c>.
    /// </summary>
    public IReadOnlyDictionary<char, IReadOnlyList<string>>? Tags { get; init; }

    /// <summary>
    /// The earliest time, in Unix seconds, included.
    /// </summary>
    public long? Since { get; init; }

    /// <summary>
    /// The latest time, in Unix seconds, included.
    /// </summary>
    public long? Until { get; init; }

    /// <summary>
    /// The most events the relay sends for it: the newest ones.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// The filter as NIP-01 writes it.
    /// </summary>
    public void Write(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        if (Kinds is not null)
        {
            writer.WriteStartArray("kinds");
            foreach (int kind in Kinds)
            {
                writer.WriteNumberValue(kind);
            }

            writer.WriteEndArray();
        }

        if (Authors is not null)
        {
            WriteStrings(writer, "authors", Authors);
        }

        foreach ((char letter, IReadOnlyList<string> values) in Tags ?? new Dictionary<char, IReadOnlyList<string>>())
        {
            WriteStrings(writer, $"#{letter}", values);
        }

        if (Since is { } since)
        {
            writer.WriteNumber("since", since);
        }

        if (Until is { } until)
        {
            writer.WriteNumber("until", until);
        }

        if (Limit is { } limit)
        {
            writer.WriteNumber("limit", limit);
        }

        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
