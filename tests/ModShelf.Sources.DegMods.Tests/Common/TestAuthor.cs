using System.Text.Json;
using System.Text.Json.Nodes;

using ModShelf.Sources.DegMods.Api;
using ModShelf.Sources.DegMods.Links;

using NBitcoin.Secp256k1;

namespace ModShelf.Sources.DegMods.Tests.Common;

/// <summary>
/// An author the tests make up, with a key of their own, who signs events as a real one would: the tests' mods, and
/// moderation when a test makes them the admin.
/// </summary>
internal sealed class TestAuthor
{
    private readonly ECPrivKey _key;

    /// <param name="seed">Tells authors apart: each seed is another key.</param>
    public TestAuthor(byte seed)
    {
        byte[] secret = new byte[32];
        secret[31] = seed;
        secret[0] = 0x11;
        _key = ECPrivKey.Create(secret);
        PublicKey = Convert.ToHexStringLower(_key.CreateXOnlyPubKey().ToBytes());
    }

    /// <summary>
    /// The author's public key, in lowercase hexadecimal.
    /// </summary>
    public string PublicKey { get; }

    public string Npub => Nip19.Npub(PublicKey);

    /// <summary>
    /// An event the author signed.
    /// </summary>
    public NostrEvent Sign(int kind, long createdAt, IEnumerable<string[]> tags, string content = "")
    {
        IReadOnlyList<IReadOnlyList<string>> tagList = [.. tags.Select(tag => (IReadOnlyList<string>)[.. tag])];
        NostrEvent unsigned = new(new string('0', 64), PublicKey, createdAt, kind, tagList, content, new string('0', 128));
        byte[] id = NostrSignatures.ComputeId(unsigned);
        SecpSchnorrSignature signature = _key.SignBIP340(id);
        return new NostrEvent(Convert.ToHexStringLower(id), PublicKey, createdAt, kind, tagList, content, Convert.ToHexStringLower(signature.ToBytes()));
    }

    /// <summary>
    /// A mod of the new kind: its name, game, files and any other tags.
    /// </summary>
    /// <param name="files">Each file's link and SHA-256, or null for none.</param>
    /// <param name="more">Other tags; a <c>published_at</c> among them replaces the one made from <paramref name="createdAt"/>.</param>
    public NostrEvent Mod(string d, string title, string game, long createdAt, IEnumerable<(string Url, string? Sha256)> files, params string[][] more)
    {
        List<string[]> tags = [["d", d]];
        if (!more.Any(tag => tag is ["published_at", ..]))
        {
            tags.Add(["published_at", createdAt.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }

        tags.AddRange([["g", game], ["title", title], ["summary", $"{title}, made up."]]);
        foreach ((string url, string? sha256) in files)
        {
            JsonObject download = new() { ["file"] = url, ["title"] = $"{title} file" };
            if (sha256 is not null)
            {
                download["hash"] = sha256;
            }

            tags.Add(["download", download.ToJsonString()]);
        }

        tags.AddRange(more);
        return Sign(DegModsLink.ModKind, createdAt, tags, $"# {title}");
    }

    /// <summary>
    /// The event as a relay sends it.
    /// </summary>
    public static JsonObject Json(NostrEvent nostrEvent)
        => new()
        {
            ["id"] = nostrEvent.Id,
            ["pubkey"] = nostrEvent.PubKey,
            ["created_at"] = nostrEvent.CreatedAt,
            ["kind"] = nostrEvent.Kind,
            ["tags"] = new JsonArray([.. nostrEvent.Tags.Select(tag => (JsonNode)new JsonArray([.. tag.Select(value => (JsonNode)JsonValue.Create(value)!)]))]),
            ["content"] = nostrEvent.Content,
            ["sig"] = nostrEvent.Sig,
        };

    /// <summary>
    /// The event a relay sent, as the source reads it.
    /// </summary>
    public static NostrEvent Read(JsonNode node)
    {
        using JsonDocument document = JsonDocument.Parse(node.ToJsonString());
        return NostrEvent.Read(document.RootElement) ?? throw new InvalidDataException("Not an event.");
    }
}
