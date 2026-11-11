namespace ModShelf.Sources.DegMods;

/// <summary>
/// How <c>AddDegModsSource()</c> sets up the DEG Mods source.
/// </summary>
public sealed record DegModsOptions
{
    /// <summary>
    /// DEG Mods' site, where its mods' and games' pages are.
    /// </summary>
    public static readonly Uri DefaultSiteAddress = new("https://degmods.com/");

    /// <summary>
    /// DEG Mods' own relays: <c>brs.degmods.com</c>, which holds its mods since Aug 1, 2026, and
    /// <c>relay.degmods.com</c>, which holds the older ones and its admin's moderation.
    /// </summary>
    public static readonly IReadOnlyList<Uri> DefaultRelays = [new("wss://brs.degmods.com/"), new("wss://relay.degmods.com/")];

    /// <summary>
    /// The Blossom file servers DEG Mods' site asks for a file by its SHA-256, besides its own.
    /// </summary>
    public static readonly IReadOnlyList<Uri> DefaultFileServers =
    [
        new("https://blossom.primal.net/"),
        new("https://blossom.data.haus/"),
        new("https://nostr.download/"),
        new("https://blossom.jumble.social/"),
        new("https://blossom-01.uid.ovh/"),
        new("https://blossom-02.uid.ovh/"),
        new("https://blossom.ditto.pub/"),
    ];

    /// <summary>
    /// DEG Mods' own file servers, which give a file only to a client that showed DEG Mods' ad: ModShelf takes their
    /// files from the other servers, or opens the mod's page.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultGatedHosts = ["brs.degmods.com", "bs.degmods.com"];

    /// <summary>
    /// DEG Mods' own account, which publishes its moderation.
    /// </summary>
    public const string DefaultAdmin = "f4bf1fb5ba8be839f70c7331733e309f780822b311f63e01f9dc8abbb428f8d5";

    /// <summary>
    /// The site, ending in a slash.
    /// </summary>
    public Uri SiteAddress { get; init; } = DefaultSiteAddress;

    /// <summary>
    /// The relays the mods and the moderation are read from, every one each time. Tests point them at recorded answers.
    /// </summary>
    public IReadOnlyList<Uri> Relays { get; init; } = DefaultRelays;

    /// <summary>
    /// The file servers a file is asked of by its SHA-256, in order, each ending in a slash.
    /// </summary>
    public IReadOnlyList<Uri> FileServers { get; init; } = DefaultFileServers;

    /// <summary>
    /// The hosts of DEG Mods' own file servers.
    /// </summary>
    public IReadOnlyList<string> GatedHosts { get; init; } = DefaultGatedHosts;

    /// <summary>
    /// The public key, in lowercase hexadecimal, of the account whose moderation is applied.
    /// </summary>
    public string Admin { get; init; } = DefaultAdmin;

    /// <summary>
    /// How long a relay has to connect, or to answer a request.
    /// </summary>
    public TimeSpan AnswerTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long every mod read at once is kept, for Browse and its searches, before it's read again.
    /// </summary>
    public TimeSpan KeepModsFor { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether its games' covers are read from DEG Mods' games database, about 28 MB once a run (ADR 0093): for a host that
    /// shows them, as the desktop app; the command line shows none.
    /// </summary>
    public bool ReadsGameCovers { get; init; } = true;
}
