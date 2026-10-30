# 17. Update feeds and watchlists

- Status: Accepted
- Date: 2026-10-26

## Context

Phase 3 ends with the two capabilities the plan lists last for the Nexus source: which mods changed lately, and the account's tracked mods. Phase 4's update checker will build on the first, and its watched mods may sync with the second. For now, the CLI shows both.

Nexus Mods' v1 API has `updated.json`, which lists a game's mods active in the last day, week or month, with each mod's last file change and last activity. It also has `tracked_mods.json`, which lists the account's tracked mods, and tracks or untracks one. It doesn't document how it answers tracking a mod already tracked, or untracking one that isn't.

## Decision

### The capabilities

**`IUpdateFeed`** lists which of a game's mods changed since a time, in one request per game rather than one per mod.

- **`Reach`** says how far back it goes. An earlier `since` is an `ArgumentOutOfRangeException`. A `since` up to a minute earlier is still taken, so a caller can ask for all of the reach with a time it read from its own clock a moment before the source reads its own.
- **`ModUpdate`** holds:
  - the mod;
  - when anything about it last changed: its files, its page or its comments;
  - when a file was last added or changed, or null when the site doesn't say. That's what an update is. It can be long before `since`, when only the page changed.
  - the mod's page, a link the source recognizes, or null. The feed gives only IDs, and the CLI shows a link per mod.

**`IWatchlistSync`** reads the mods the linked account follows on the site, and follows or stops following one.

- **Following a mod already followed, or unfollowing one that isn't, changes nothing** and is no error.
- `GetWatchedAsync` returns `ModRef`s, of every game.

**The shared contract tests** check that a source:

- refuses another source's game or mod;
- refuses to reach further back than it says;
- reports only the game's mods active since the time asked;
- lists a mod it watches until it unwatches it, watching and unwatching twice each. The source's own test sets up a site that keeps the list.

### The Nexus source

**Updates:**

- **The period.** It asks `updated.json` for the shortest period that covers `since`: `1d` up to a day back, `1w` up to a week, otherwise `1m`. The recorded `1d` answer covers 24 hours back from when it was recorded, so the periods are taken as rolling.
- **The reach is 28 days**, the shortest month.
- **What it keeps.** The period can reach further back than asked, so it keeps the mods active since `since`. It skips an entry without a positive mod ID or an activity time. A file time of 0 means none.
- **The page** is `https://www.nexusmods.com/{game}/mods/{mod}`.
- **The time.** The source now takes the registered `TimeProvider`.

**Tracked mods:**

- **Listing.** `GET tracked_mods.json` gives `{mod_id, domain_name}` entries. Each mod is listed once, with its game in lower case. An entry without a game or ID Nexus Mods gives is skipped.
- **Changing.** Tracking is a form `POST` to `tracked_mods.json`, and untracking a form `DELETE`. The game goes in the `domain_name` query parameter and the mod in the `mod_id` form field, as other clients of the API send them.
- **Refusals.** When Nexus Mods refuses the request itself (a 4xx other than 429), the source asks for the tracked mods. If the mod already is as asked, there's no error; otherwise the refusal is passed on.
  - A 401 is a refused account, and is passed on.
  - A 429 or a server error is passed on without asking again.
  - So tracking a mod that doesn't exist still fails with 404.
- **Answers without a body.** The API client can now send a request whose answer doesn't matter, and checks only the status.

**Recording.** `-Tracking` in the recording script records, for the mod given to `-ModId`:

- tracking it, and tracking it again;
- untracking it, and untracking it again;
- tracking a mod that doesn't exist;
- the tracked mods while it's tracked, trimmed to that one mod.

It works with either account and records only those, or the searches too with `-SearchOnly`.

- **It's the only option that changes the account.** It starts by untracking the mod if the account already tracks it. Whatever happens, the tracked mods end as they started, with a warning if they can't be put back.
- Each recording keeps the form it sent.
- A user ID inside a message's text is now replaced too, such as in "User 1234567 is now Tracking Mod: 2400".

### Application

- **`IModUpdates`** passes the question to the update feed of the game's source and puts the most recently active mods first. `Sources` lists the sources with a feed.
- **`IWatchlists`** lists a source's followed mods, and follows or stops following the mod a link is to. It finds the link's source the way `IModLookup` does. A link no source knows, or whose site has no watchlist, is an `UnsupportedLinkException`. `Sources` lists the sources with a watchlist.
- `AddModUpdates()` and `AddWatchlists()` register them.

### The CLI

- **`mod updates --game <game> [--since …]`** lists the mods of the game that changed since then, in one request. Each row has the mod's ID, when its files last changed, when it was last active, and its link, the most recently active first.
  - Mods with new or changed files since then are marked; the others only changed their pages.
  - `--since` takes `1d` (the default), `7d` or `28d`, or an ISO date or time. Times are local unless they give an offset, and a date means its start.
  - A time still to come is refused. A time beyond the feed's reach gets the source's message, such as "Nexus Mods lists the mods updated in the last 28 days only."
- **`mod tracked`** lists the mods the linked account tracks on the site, by game and then by number.
- **`mod track <link>`** and **`mod untrack <link>`** track the mod a link is to, or stop tracking it.
- **`--source`** picks the site when more than one can do it, as for `mod search`. The messages for choosing a site are shared.

## Consequences

- **The update checker (Phase 4)** can check a game's mods with one request when they were last checked within the feed's reach, and fetch files only for the mods whose files changed. Mods last checked longer ago need checking one by one.
- **A second source** implements `IUpdateFeed` with its own reach, or leaves it out if its site has no such list. It implements `IWatchlistSync` if its site has follows or subscriptions, such as Modrinth's followed projects or mod.io's subscriptions. It passes the same contract tests.
- **`mod tracked` shows game and mod IDs, but no links**, since `GetWatchedAsync` returns `ModRef`s. Showing links would need the watchlist to return a record with a page, as `ModUpdate` does.
- **Tracking from a link to a mod's page** asks for the mod's files, as recognizing such a link always does, to pick its newest main file. So a mod without main files needs a link to one of its files.

## Open

- **How Nexus Mods answers tracking and untracking.** Recorded on 2026-10-30 with `-Tracking`: tracking answers 201, and 201 again for a mod tracked already; untracking answers 200, and 404, "User is not tracking mod. Unable to untrack.", for a mod not tracked; tracking a mod that doesn't exist answers 404, "Mod: 999999999 not found". Tests replay each, matched by method and form. The check against the list stays for every refusal but a rate limit, a refused account and a server error: it costs a request only when something is refused, and covers statuses the recording didn't show.
- **Whether `1m` reaches back 30 days or a calendar month.** The reach stays at 28 days either way.
