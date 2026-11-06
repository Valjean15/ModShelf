# 77. Quota warning

- Status: Accepted
- Date: 2026-11-06

## Context

The sites allow an account so many requests an hour or a day, and Nexus Mods says what's left with each answer. Since
Phase 3, a source with a quota implements `IApiQuota`, and the Accounts screen shows what's left; nothing else did, so
a check or a batch of downloads could use up the hour's requests unseen, and the rest waited.

The plan (step 17): when a site's requests run low, such as under 10% of the hour's or the day's, a calm strip at the
top says so, with when they reset; a big check or batch that would likely run out says so before it starts, and offers
to go on; Accounts keeps the full numbers; the level that counts as low is a setting. The approved mockups (step 8): the
strip calm while low and warm once used up, dismissible until they reset, warning below 10% by default.

The human chose (2026-11-06) the setting **`quota.warnBelow`**, a percent, **10** by default, **0** for never.

## Decision

### The setting

- `AppSettings.QuotaWarnBelow` (`int?`, null for `DefaultQuotaWarnBelow`, 10; from 0 to `MaxQuotaWarnBelow`, 90) and
  `ISettingsService.SetQuotaWarnBelowAsync`, public additions the choice of a setting approved; `"quota": { "warnBelow":
  15 }` in `settings.json`; `settings set quota.warnBelow 15` (a `%` after it is fine) and `settings reset` on the
  command line; Settings › Updates, **Requests to the sites**: Never, or when below 5, 10 (the default), 20 or 30% are
  left, and a share set elsewhere too.

### The strip

- `QuotaWarningViewModel` (Presentation's `ViewModels/Quota/`), a singleton the main window shows (`Quota`, a new
  constructor parameter) and starts with the screens. Every 30 seconds, on the UI thread, it reads the setting and each
  `IApiQuota` source's quota; the sites say what's left with each answer, and nothing else tells when it changes.
- Of a site's allowances that haven't renewed, the one with the smallest share left counts, when that's below the
  setting: "Nexus Mods has 42 of its 500 requests left this hour. They renew at 4:00 PM." (or "today", or neither for
  another period); used up, warm: "The requests to Nexus Mods are used up until 4:00 PM. Checks and downloads from it
  wait until then." A renewal on another day says its day, "Sat 6:00 AM", in the user's time.
- One site at a time, the first that's low. Its × hides it until the allowance shown renews; another site's still shows.
- Settings that can't be read warn below the default, logged once (event 958). With the warning at 0, nothing shows,
  not even used up.

### Asking first

- `QuotaChecks.GoOnAsync` (Presentation's `Common`): before the work, for each site, the requests it would likely take
  against what's left of the site's tightest allowance that hasn't renewed; when more, it asks ("Requests may run
  out": about how many it may take, how many are left this hour or today, and when they renew; **Go on** or **Cancel**).
  A site that doesn't say what's left goes on at once.
- Estimates, from what the screens know, as the sites' answers vary:
  - a check: one request for each game with watched mods (its feed), and one for each watched mod never checked or due
    a full check, which is asked about on its own;
  - a download into the library: two (the mod's files, then the file's link).
- Asked before the Updates screen's **Check now** and **Update all** (and a game's), the Library's **Update all** on a
  game and **Update selected**, and Browse's **Download selected**. Background checks and single downloads don't ask.

### Desktop

- `MainWindow`: over the screen shown, `Border.quotaStrip` at the top (calm: raised; `.usedUp`: the warm soft colour),
  its message read out as it changes, and a × to hide it.

## Consequences

- No database or source change: `IApiQuota` already said what's left.
- What's shown is up to 30 seconds old, and as the site last said.
- The estimates can be off either way; the question says "about".

## Testing

- The setting: Application's range and default; `settings.json` read, written, refused out of range, and left out at
  its default; the command line's set, off, reset, refusals and the list of settings; the Settings screen's choices and
  what they save, and its part in the Updates category.
- The strip: nothing while enough is left or nothing's said; the hour's and the day's, with the day of a renewal on
  another day; used up; the setting's share; off; dismissed until renewed; one site dismissed and another shown; an
  allowance renewed not counted; every 30 seconds on the UI thread; settings that can't be read logged once.
- Asking: going on with enough left or nothing said; asking and going on as answered; the day's; a renewed allowance;
  another period; Check now asking or not, Update all on Updates and in the Library, and Download selected in Browse.
- 36 deliberate bugs, all caught. A 37th, the strip looking at the sites with the warning at 0, changed nothing, as no
  share left is below 0%.
- Looked at here, on Linux: Settings › Updates' new part, found through quick search with "requests". Without an
  account, no site says what's left, so the strip and the question were seen only in the tests.
- By hand: `docs/manual-checklist-desktop.md` 2.5a.
