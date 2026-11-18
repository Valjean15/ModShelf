namespace ModShelf.Sources.Abstractions.Common;

/// <summary>
/// What the site calls following a mod with the account and approving one, in its own words, which the screens put in
/// their own phrases, such as "Track on Nexus Mods" or "87 likes". Each word is lowercase, as in the middle of a
/// sentence; the screens capitalize it where a sentence starts. A word is null where the site has no such thing.
/// </summary>
public sealed record SiteWords
{
    /// <summary>
    /// A site that neither follows mods nor counts approval.
    /// </summary>
    public static SiteWords None { get; } = new();

    /// <summary>
    /// Following a mod, as <see cref="Watchlist.IWatchlistSync.WatchAsync"/> does, such as "track", "subscribe" or
    /// "follow".
    /// </summary>
    public string? Follow { get; init => field = Word(value); }

    /// <summary>
    /// Following one no longer, as <see cref="Watchlist.IWatchlistSync.UnwatchAsync"/> does, such as "untrack",
    /// "unsubscribe" or "unfollow".
    /// </summary>
    public string? Unfollow { get; init => field = Word(value); }

    /// <summary>
    /// A mod followed, as in "tracked mods", "subscribed mods" or "followed mods".
    /// </summary>
    public string? Followed { get; init => field = Word(value); }

    /// <summary>
    /// One of what <see cref="Catalog.ModInfo.Endorsements"/> counts, as in "1 endorsement" or "1 like".
    /// </summary>
    public string? Approval { get; init => field = Word(value); }

    /// <summary>
    /// More than one of what <see cref="Catalog.ModInfo.Endorsements"/> counts, as in "87 endorsements" or "87 likes".
    /// </summary>
    public string? Approvals { get; init => field = Word(value); }

    /// <summary>
    /// Approving a mod with the account, as <see cref="Endorsements.IModEndorsements.EndorseAsync"/> does, such as
    /// "endorse".
    /// </summary>
    public string? Approve { get; init => field = Word(value); }

    /// <summary>
    /// Choosing not to approve a mod, or taking an approval back, such as "abstain".
    /// </summary>
    public string? Unapprove { get; init => field = Word(value); }

    /// <summary>
    /// Whether it has the words for following a mod: all three, or none.
    /// </summary>
    public bool CanFollow => Follow is not null && Unfollow is not null && Followed is not null;

    /// <summary>
    /// Whether it has the words for approval: one and more, or none.
    /// </summary>
    public bool CountsApproval => Approval is not null && Approvals is not null;

    /// <summary>
    /// Whether it has the words for approving a mod with the account: both, or none.
    /// </summary>
    public bool CanApprove => Approve is not null && Unapprove is not null;

    private static string? Word(string? word) => string.IsNullOrWhiteSpace(word) ? null : word.Trim();
}
