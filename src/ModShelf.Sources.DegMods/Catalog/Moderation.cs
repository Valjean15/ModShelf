using ModShelf.Sources.DegMods.Api;

namespace ModShelf.Sources.DegMods.Catalog;

/// <summary>
/// DEG Mods' moderation, as its admin publishes it and its site applies it by default: the mods it blocks, the authors it
/// blocks, the tags whose mods it hides, and the mods it marks for adults that their authors didn't. ModShelf always
/// applies all of it, with no setting (ADR 0090), and hides mods sexualizing minors whatever the admin's list says.
/// </summary>
/// <param name="HiddenTags">The tags whose mods are left out, lowercase.</param>
/// <param name="BlockedMods">The mods left out, by their ID in ModShelf.</param>
/// <param name="BlockedAuthors">The authors whose mods are left out, by public key.</param>
/// <param name="MarkedAdult">The mods the admin marked for adults, by their ID in ModShelf.</param>
internal sealed record Moderation(
    IReadOnlySet<string> HiddenTags,
    IReadOnlySet<string> BlockedMods,
    IReadOnlySet<string> BlockedAuthors,
    IReadOnlySet<string> MarkedAdult)
{
    /// <summary>
    /// The admin's list of tags hidden by default (kind 30078).
    /// </summary>
    public const string HiddenTagsList = "moderation-excluded-tags";

    /// <summary>
    /// The admin's list of blocked mods (kind 30078).
    /// </summary>
    public const string BlockedModsList = "blocked-mods";

    /// <summary>
    /// The label of the admin's moderation tags (kind 30985).
    /// </summary>
    public const string Label = "moderation";

    public const int ListKind = 30078;

    public const int BlockedAuthorsKind = 10000;

    public const int MarkKind = 30985;

    /// <summary>
    /// The tags DEG Mods' site hides when its admin's list can't be read.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultHiddenTags = ["politics", "religion", "loli", "shota", "gore"];

    /// <summary>
    /// The tags ModShelf always hides, whatever the admin's list says: mods that sexualize minors.
    /// </summary>
    public static readonly IReadOnlyList<string> AlwaysHiddenTags = ["loli", "shota"];

    /// <summary>
    /// What DEG Mods' site applies before it has read its admin's lists: its default hidden tags, nothing blocked.
    /// </summary>
    public static Moderation Default { get; } = new(
        DefaultHiddenTags.ToHashSet(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// The moderation the admin's events publish: the newest of each list. A list the admin hasn't published is what the
    /// site applies without it.
    /// </summary>
    public static Moderation Read(IEnumerable<NostrEvent> events, string admin)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentException.ThrowIfNullOrWhiteSpace(admin);

        Dictionary<(int Kind, string D), NostrEvent> newest = [];
        foreach (NostrEvent nostrEvent in events.Where(nostrEvent => nostrEvent.PubKey == admin))
        {
            (int, string) key = (nostrEvent.Kind, nostrEvent.Kind == BlockedAuthorsKind ? string.Empty : nostrEvent.D);
            if (!newest.TryGetValue(key, out NostrEvent? seen) || nostrEvent.CreatedAt > seen.CreatedAt)
            {
                newest[key] = nostrEvent;
            }
        }

        IEnumerable<string> hidden = newest.TryGetValue((ListKind, HiddenTagsList), out NostrEvent? tags)
            ? tags.FirstValues("t").Select(tag => tag.Trim().ToLowerInvariant()).Where(tag => tag.Length > 0)
            : DefaultHiddenTags;
        IEnumerable<string> blockedMods = newest.TryGetValue((ListKind, BlockedModsList), out NostrEvent? mods)
            ? mods.FirstValues("a").Select(ModKey).OfType<string>()
            : [];
        IEnumerable<string> blockedAuthors = newest.TryGetValue((BlockedAuthorsKind, string.Empty), out NostrEvent? authors)
            ? authors.FirstValues("p").Where(key => key.Length == 64 && ModEvents.IsHex(key)).Select(key => key.ToLowerInvariant())
            : [];
        IEnumerable<string> adult = newest
            .Where(entry => entry.Key.Kind == MarkKind && entry.Value.Tag("content-warning") is not null)
            .Select(entry => ModKey(entry.Key.D))
            .OfType<string>();

        return new Moderation(
            hidden.ToHashSet(StringComparer.Ordinal),
            blockedMods.ToHashSet(StringComparer.Ordinal),
            blockedAuthors.ToHashSet(StringComparer.Ordinal),
            adult.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// Whether the mod is left out: blocked, by a blocked author, with a hidden tag, or with a tag ModShelf always hides.
    /// </summary>
    public bool Hides(DegMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        return BlockedMods.Contains(mod.Key)
            || BlockedAuthors.Contains(mod.Author)
            || mod.Tags.Overlaps(HiddenTags)
            || mod.Tags.Overlaps(AlwaysHiddenTags);
    }

    /// <summary>
    /// The mods that aren't left out, each marked for adults when the admin marked it.
    /// </summary>
    public IReadOnlyList<DegMod> Apply(IEnumerable<DegMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);

        return [.. mods.Where(mod => !Hides(mod)).Select(Apply)];
    }

    /// <summary>
    /// The mod, marked for adults when the admin marked it.
    /// </summary>
    public DegMod Apply(DegMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        return !mod.IsAdult && MarkedAdult.Contains(mod.Key) ? mod with { IsAdult = true } : mod;
    }

    /// <summary>
    /// A mod's ID in ModShelf from its address, <c>&lt;kind&gt;:&lt;author&gt;:&lt;d&gt;</c>, of either kind of mod; null
    /// for another kind's.
    /// </summary>
    private static string? ModKey(string address)
    {
        string[] parts = address.Split(':', 3);
        return parts.Length == 3
            && parts[0] is "31142" or "30402"
            && ModEvents.TryReadKey($"{parts[1].ToLowerInvariant()}:{parts[2]}", out string author, out string d)
                ? ModEvents.Key(author, d)
                : null;
    }
}
