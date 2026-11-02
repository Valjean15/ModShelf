# 47. Pictures and saved mod details

- Status: Accepted
- Date: 2026-11-02

## Context

The 0.9.3 canvas shows mods with pictures: a thumbnail beside each row, or cards with a large picture, chosen per screen
under "Previews". The plan's step 7 builds what that needs before the screens that use it most (the mod page, Library
by game): pictures loaded and saved, mods' details saved so the screens open at once and offline, the Previews setting,
and the Settings card for them. The human approved four choices:

- our own picture loader, rather than an Avalonia image-loading package;
- Nexus Mods' blurred thumbnails for adult mods, where it gives them;
- mods' details saved in the library database, with a new migration;
- the previews as on the canvas.

Nexus Mods' GraphQL API (v2), which searches use, gives a mod's `thumbnailUrl` and `thumbnailBlurredUrl`. Its REST API
(v1), which the library's mods are read with, gives the full `picture_url` only. Step 8 moves mods' details to v2, which
needs a new recording with the human's account.

## Decision

- **Thumbnails in the catalog.** `ModInfo` gains `ThumbnailUrl` and `ThumbnailBlurredUrl`, optional, checked as web
  addresses like its other links. Nexus Mods' search asks for both. A source without them leaves them null.
- **Saved pictures**, `IPictureCache` (Application) and `DiskPictureCache` (Infrastructure):
  - `GetAsync(address)` gives a picture's file, downloading it the first time through `IHttpClientFactory`, with two
    retries: images only, 10 MB at most, 30 seconds each; written beside its place, then moved there, so a half-written
    picture is never shown. Six download at once, and callers asking for one meanwhile share its download, which one
    caller giving up doesn't stop. A picture that can't be downloaded gives null and is asked for again next time.
  - Kept in a `pictures` folder beside the library database, each named by the SHA-256 of its address. A picture
    shown is touched; when they outgrow `pictures.maxSize` (512 MB), the ones shown least recently go, down to nine
    tenths of it. `GetSizeAsync` and `ClearAsync` serve the Settings card.
  - Logs name the site, never the picture's address; the factory's request logging is off for this client.
- **Saved mod details**, `IModDetails` (Application), its port `IModDetailsRepository`, and the `ModDetails` table
  (migration `AddModDetails`, one row per mod, keyed by source, game and ID, apart from the library's tables):
  - `GetSavedAsync` gives the saved details, however old, to show at once. `GetAsync` gives them while they're younger
    than `details.maxAge` (a day), otherwise the site's, which are saved; the saved ones, however old, when the site
    can't say (offline, refused, timed out, a mod it doesn't know, or a source that can't describe its mods).
  - One read of a mod serves every caller asking meanwhile, and four run at once, so a library opening doesn't ask its
    site about all its mods together. Details saved "in the future", by a clock since set back, count as old.
  - Details are a convenience: a row that can't be read is read from the site again, and a database that can't be read
    or written for them is logged, never thrown.
- **Settings:** `previews.<library|browse|updates|downloads>` (`off`, `thumbnails`, `cards`; Library thumbnails, Browse
  cards, the others off), `pictures.maxSize` (64–8192 MB), `pictures.adult` (`hidden`, `blurred`, `shown`; blurred) and
  `details.maxAge` (15 minutes to 7 days), in `settings.json` under `previews`, `pictures` and `details`, and in the CLI.
- **Presentation:**
  - `ModPictures`, one for the app: each screen's previews and how adult pictures look, read from the settings once;
    a change made through it is saved and tells every screen.
  - `PictureChoice` picks a mod's picture: its thumbnail, or its full picture when the site gives no thumbnail. An adult
    mod's is hidden, the site's blurred thumbnail, or the picture blurred by the view, or shown, as the setting says.
  - `ModPictureViewModel`, a mod's picture on a row or card: the saved details' at once, then the site's when they're
    old; a picture asked for later replaces one still being read.
  - `PreviewsViewModel`, a screen's Off, Thumbnails and Cards; Updates and Downloads offer no cards, and show thumbnails
    for a setting that says cards.
  - Library and Updates take their mods' pictures from `IModDetails`, Downloads too, for files of a mod on a site;
    Browse from the search's own details. Pictures are read only while a screen shows them.
  - The Settings screen's "Pictures and mod details" card: the room taken and the limit (256 MB to 2 GB), **Clear**,
    how long details are kept (6 hours, a day, a week), and adult content.
- **Desktop:**
  - The shared parts the plan asks for: `ModThumbnail` (72 by 40), `ModCardPicture` (sixteen by nine, saying "Adult ·
    blurred" on a blurred one), `PreviewsPicker`, and the card styles.
  - `ModImage` decodes a picture off the UI thread at about twice the width it's shown at, and keeps the last 300 decoded.
    A picture to blur is decoded twelve pixels wide and scaled up smoothly, so nothing in it can be made out and the
    sharp picture is never drawn.
  - Library cards go under each game's heading, as many side by side as fit at 220 wide or more; Browse's cards open
    their mod. In a narrow window, the previews go below the filter or the heading, and an update's buttons below it.

## Consequences

- With Library and Browse showing pictures by default, opening them reads a few pictures from Nexus Mods' file servers,
  which aren't API requests; mods' details are, one per library mod a day, four at once.
- Until step 8, library mods' thumbnails are their full pictures made small, a few hundred kilobytes to a few megabytes
  each, so the 512 MB fills sooner; and their adult pictures are blurred by the view, with no site-blurred thumbnail.
- Cards don't list a mod's files: the rows do, until step 9 gives each mod a page of its own.
- `ModInfo` and the settings grew; a source adding pictures needs no other change.

## Testing

- Infrastructure: the picture cache over a scripted site (once, shared, six at once, refused, not a picture, too large
  by its header or its body, offline then online, logs, trimming by last shown, the picture just saved kept when others
  seem shown later, settings that can't be read, size and clearing), and the details repository over a real SQLite file
  (a restart, replacing, keys, a bad row, a missing table).
- Application: `ModDetails` over a fake catalog and repository (fresh, old, the setting, a clock set back, each site
  problem, no catalog, a bad row, shared reads, four at once).
- Settings and the CLI: each new setting read, written, validated and listed.
- Presentation: `PictureChoice`, `ModPictureViewModel`, `PreviewsViewModel` and `ModPictures`, the pictures card, and
  previews on Library, Browse, Updates and Downloads.
- Desktop: screenshots on Xvfb over a seeded library with drawn pictures, dark and light: thumbnails, cards, blurred by
  the site and by the view, hidden, none, the Settings card, and the smallest window.
- 47 deliberate bugs in the cache, the details and their table, the settings, the picture choice, the previews and the
  Settings card, each caught by a test.
- By hand: `docs/manual-checklist-desktop.md` 3.8, 3.9, 4.6 and 10.6a.
