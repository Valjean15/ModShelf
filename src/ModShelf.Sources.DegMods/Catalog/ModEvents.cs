using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ModShelf.Sources.DegMods.Api;
using ModShelf.Sources.DegMods.Links;

namespace ModShelf.Sources.DegMods.Catalog;

/// <summary>
/// Mods, read from their events as DEG Mods' site reads them: each author's mod by its <c>d</c> tag, its newest event of
/// kind 31142 when it has one, or else its newest older listing (kind 30402, tagged <c>t=GameMod</c>) from before Aug 1,
/// 2026, unless that one says it moved to the new kind (<c>legacy</c> <c>yes</c>). A mod deleted by its author, or without a
/// name or a game, isn't one.
/// </summary>
internal static class ModEvents
{
    /// <summary>
    /// When DEG Mods stopped reading its older listings, Aug 1, 2026, in Unix seconds: one made since isn't a mod.
    /// </summary>
    public const long OldModsEnd = 1785542400;

    /// <summary>
    /// The tag an older listing of a mod carries.
    /// </summary>
    public const string OldModTag = "GameMod";

    private const int MaxD = 255;

    private static readonly SearchValues<char> LowercaseHex = SearchValues.Create("0123456789abcdef");

    /// <summary>
    /// A mod's ID in ModShelf: its author and its <c>d</c> tag.
    /// </summary>
    public static string Key(string author, string d) => $"{author}:{d}";

    /// <summary>
    /// The author and <c>d</c> tag of a mod's ID in ModShelf; false for anything else.
    /// </summary>
    public static bool TryReadKey(string? key, out string author, out string d)
    {
        author = string.Empty;
        d = string.Empty;
        if (key is null || key.Length < 66 || key[64] != ':' || key.AsSpan(0, 64).ContainsAnyExcept(LowercaseHex))
        {
            return false;
        }

        author = key[..64];
        d = key[65..];
        return Encoding.UTF8.GetByteCount(d) <= MaxD;
    }

    /// <summary>
    /// A game's key in ModShelf: its name as DEG Mods writes it, in lowercase, since DEG Mods tells its games apart that
    /// way.
    /// </summary>
    public static string GameKey(string name) => name.Trim().ToLowerInvariant();

    /// <summary>
    /// The mods the events are of, each once.
    /// </summary>
    public static IReadOnlyList<DegMod> Read(IEnumerable<NostrEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        Dictionary<string, NostrEvent> newest = new(StringComparer.Ordinal);
        Dictionary<string, NostrEvent> newestOld = new(StringComparer.Ordinal);
        foreach (NostrEvent nostrEvent in events)
        {
            Dictionary<string, NostrEvent>? into = nostrEvent.Kind switch
            {
                DegModsLink.ModKind => newest,
                DegModsLink.OldModKind when IsOldMod(nostrEvent) => newestOld,
                _ => null,
            };
            string key = Key(nostrEvent.PubKey, nostrEvent.D);
            if (into is not null && (!into.TryGetValue(key, out NostrEvent? seen) || Later(nostrEvent, seen)))
            {
                into[key] = nostrEvent;
            }
        }

        foreach ((string key, NostrEvent old) in newestOld)
        {
            newest.TryAdd(key, old);
        }

        return [.. newest.Values.Select(Read).OfType<DegMod>()];
    }

    /// <summary>
    /// The mod an event says, or null when it says none: no <c>d</c> tag, name or game, deleted, or an older listing that
    /// moved.
    /// </summary>
    public static DegMod? Read(NostrEvent nostrEvent)
    {
        ArgumentNullException.ThrowIfNull(nostrEvent);

        bool isNew = nostrEvent.Kind == DegModsLink.ModKind;
        if ((!isNew && !(nostrEvent.Kind == DegModsLink.OldModKind && IsOldMod(nostrEvent)))
            || string.IsNullOrWhiteSpace(nostrEvent.D)
            || Encoding.UTF8.GetByteCount(nostrEvent.D) > MaxD
            || nostrEvent.Tag("deleted") == "true"
            || (!isNew && nostrEvent.Tag("legacy") == "yes")
            || Text(nostrEvent.Tag("title")) is not { } title
            || Text(nostrEvent.Tag(isNew ? "g" : "game")) is not { } game)
        {
            return null;
        }

        IEnumerable<string> tags = isNew ? nostrEvent.FirstValues("t") : nostrEvent.Values("tags");
        DateTimeOffset updated = DateTimeOffset.FromUnixTimeSeconds(nostrEvent.CreatedAt);
        return new DegMod
        {
            Kind = nostrEvent.Kind,
            Author = nostrEvent.PubKey,
            D = nostrEvent.D,
            Title = title,
            Summary = Text(nostrEvent.Tag("summary")),
            Game = game,
            Picture = WebLink(nostrEvent.Tag(isNew ? "image" : "featuredImageUrl")),
            Tags = tags.Select(tag => tag.Trim().ToLowerInvariant()).Where(tag => tag.Length > 0).ToHashSet(StringComparer.Ordinal),
            IsAdult = nostrEvent.Tag("content-warning") is not null || nostrEvent.Tag("nsfw") == "true",
            PublishedAt = long.TryParse(nostrEvent.Tag("published_at"), NumberStyles.None, CultureInfo.InvariantCulture, out long published) && published > 0
                ? DateTimeOffset.FromUnixTimeSeconds(Math.Min(published, nostrEvent.CreatedAt))
                : updated,
            UpdatedAt = updated,
            Downloads = Downloads(nostrEvent, isNew),
        };
    }

    /// <summary>
    /// An absolute HTTP or HTTPS link, or null.
    /// </summary>
    public static Uri? WebLink(string? text)
        => Uri.TryCreate(text?.Trim(), UriKind.Absolute, out Uri? link) && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
            ? link
            : null;

    /// <summary>
    /// Whether the text is lowercase or uppercase hexadecimal.
    /// </summary>
    public static bool IsHex(ReadOnlySpan<char> text)
    {
        foreach (char character in text)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOldMod(NostrEvent nostrEvent)
        => nostrEvent.CreatedAt < OldModsEnd && nostrEvent.FirstValues("t").Contains(OldModTag, StringComparer.Ordinal);

    /// <summary>
    /// Whether an event replaces another of the same mod: made later, or at the same second with the lower ID, as NIP-01
    /// keeps.
    /// </summary>
    private static bool Later(NostrEvent nostrEvent, NostrEvent seen)
        => nostrEvent.CreatedAt > seen.CreatedAt
            || (nostrEvent.CreatedAt == seen.CreatedAt && string.CompareOrdinal(nostrEvent.Id, seen.Id) < 0);

    private static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// The mod's files, each once, in the author's order: from <c>download</c> tags, or <c>downloads</c> ones written
    /// as <c>name:value</c>, in a new event; from <c>downloadUrls</c> in an older listing.
    /// </summary>
    private static List<DegDownload> Downloads(NostrEvent nostrEvent, bool isNew)
    {
        IEnumerable<IReadOnlyDictionary<string, string>> listed = isNew
            ? nostrEvent.FirstValues("download").Select(Fields)
                .Concat(nostrEvent.Tags.Where(tag => tag.Count >= 2 && tag[0] == "downloads").Select(Pairs))
            : nostrEvent.Values("downloadUrls").Select(Fields);

        List<DegDownload> downloads = [];
        foreach (IReadOnlyDictionary<string, string> fields in listed)
        {
            if (WebLink(fields.GetValueOrDefault(isNew ? "file" : "url")) is not { } url)
            {
                continue;
            }

            string? sha256 = fields.GetValueOrDefault("hash")?.Trim() is { Length: 64 } hash && IsHex(hash) ? hash.ToLowerInvariant() : null;
            string id = DegModsLink.FileIdOf(sha256 ?? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri))));
            if (downloads.Exists(download => download.Id == id))
            {
                continue;
            }

            downloads.Add(new DegDownload(
                id,
                url,
                sha256,
                Text(fields.GetValueOrDefault("title")),
                Text(fields.GetValueOrDefault("filename")),
                Text(fields.GetValueOrDefault(isNew ? "version" : "modVersion"))));
        }

        return downloads;
    }

    /// <summary>
    /// A file's fields, from the JSON object an event lists it as; none when it isn't one.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Fields(string json)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return fields;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    fields.TryAdd(property.Name, property.Value.GetString()!);
                }
            }
        }
        catch (JsonException)
        {
            // Not a file.
        }

        return fields;
    }

    /// <summary>
    /// A file's fields, from a tag's values written as <c>name:value</c>.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Pairs(IReadOnlyList<string> tag)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (string value in tag.Skip(1))
        {
            int colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                fields.TryAdd(value[..colon], value[(colon + 1)..]);
            }
        }

        return fields;
    }
}
