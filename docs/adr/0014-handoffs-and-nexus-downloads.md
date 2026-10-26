# 14. Handoffs and Nexus Mods downloads

- Status: Accepted
- Date: 2026-10-26

## Context

Nexus Mods gives a download link through its API straight away only to Premium accounts. A free account has to start each download on the website, with the "Mod Manager Download" (the "Slow download" on the page opened with `nmm=1`). The website then hands the browser an `nxm://` link, such as `nxm://stardewvalley/mods/2400/files/160380?key=…&expires=…&user_id=…`, and the API gives a download link for the key and expiry in it. The recorded free account gets 403 from `download_link.json` without them.

Phase 1 already had the AwaitingHandoff status (ADR 0002) and `HandoffRequired` with the page to open, but no way to get the handed-over link back to the waiting job. The desktop app will receive `nxm://` links once it registers as their handler, in Phase 6. Until then the CLI is the only way in, so the user pastes the link.

## Decision

### Shared contracts

- **`RecognizedLink.IsHandoff`** marks a link that completes a handoff, such as an `nxm://` link. A catch-all match can't be one.
- **`IDownloadResolver.ResolveAsync(file, handoff, …)`** takes the handoff link, or null. The link can carry a key, so it's never stored or logged. A source refusing it answers `HandoffRequired` again.
- **`IDownloadService.HandOffAsync(file, link, …)`** gives the queue a link for a file. It's keyed by file rather than by job, so a link that arrives before its job, or for a file several jobs download, still reaches them.
- **`ILinkDownloads.AddAsync`** hands a link that completes a handoff to the queue before finding or adding the job, so the job resolves with it from its first attempt.
- **`DownloadJobSnapshot.HandoffPage`** is the page to start the download on, while the job waits.
- **`DownloadFailureReason.AccountNeeded` (7)**: the source needs a linked account, or refused the one linked. A source reports it by throwing `AuthenticationFailedException`. A 404 or 410 becomes LinkUnavailable, and any other `HttpRequestException` becomes Network.

### The scheduler

- **Handoff links and pages live in memory only.** Links can carry a key, and pages come from the source when it's asked.
- A job waiting for a handoff **resolves again** when a link for its file arrives, or when the scheduler doesn't know its page. Waiting jobs go before queued ones, because a link that has just arrived may expire soon.
- **After a restart, waiting jobs ask their source again**, since their pages are gone. The status still survives a restart, as ADR 0002 says, but the scheduler no longer leaves the job alone: a source that no longer needs the handoff, such as an account upgraded to Premium, downloads the file.
- **A refused link is dropped**, and the job waits for a new one instead of asking again in a loop. A link that arrived while the source was being asked is a new one, so the job asks again with it straight away, without showing the page.
- **A link is forgotten** once no unfinished job for its file is left. A failed job keeps it, so a retry can use it.
- A pause or cancel requested while the source is asked **applies when the source asks for a handoff**, as it does when a download stops.

### The Nexus source

**Links it recognizes:**

- a mod's page on `www.nexusmods.com` or `nexusmods.com`: `/{game}/mods/{mod}`;
- a file on its Files tab: the same page with `file_id`;
- `nxm://{game}/mods/{mod}/files/{file}`, which completes a handoff.

The game is lower-cased and limited to letters, digits, `-` and `_`. IDs are positive whole numbers without leading zeros. Its own links to anything else, such as news pages, are refused with what to copy instead; collections, on the website or as `nxm://` links, are refused as collections. Other sites' links, including Nexus Mods' CDN, are left to other sources without asking the API.

`next.nexusmods.com` doesn't serve mod pages: checked on 2026-10-26, `/stardewvalley/mods/2400` there fails, and `/games/stardewvalley` redirects to `www`. It hosts collections, though, so its links are read too. Its links to a mod or file get the same IDs as on `www`, and its collections get the refusal instead of being fetched as a web page by the direct-link source.

**What it asks the API when recognizing:**

- A file link asks `files/{file}.json` for the file's name.
- A link to the mod as a whole asks `files.json`, and means the newest main file (category 1, uploaded last, then the highest ID). The CLI names the file it's downloading, which says which one was picked.
- Names are made safe for every system, as the direct-link source does.

**Resolving a download:**

- It asks `download_link.json`, and returns a ticket for the first server offered, Nexus Mods' own CDN. The ticket expires when the link's `expires` parameter says. The CDN link's `md5` parameter signs the link; it isn't the file's hash, so no hash is checked.
- **403 without a key** means a free account: the job waits for a handoff on `https://www.nexusmods.com/{game}/mods/{mod}?tab=files&file_id={file}&nmm=1`.
- **With a handoff**, its `key` and `expires` go with the request. Nexus Mods doesn't document how it refuses an expired or foreign key, so a 400, 403 or 410 counts as a refused key. The account is then tried without it, so an expired key never stops a Premium account, and a free account is asked to start the download again.
- **The handoff must be an `nxm://` link to the same file**, and the file's IDs ones Nexus Mods gives, before any request is made.
- Links are never kept: the engine asks again every time.

**Logging.** The HTTP client factory's own logging masks query strings in .NET 9 and later, so the key never reaches the logs. A test keeps it that way.

### The CLI

- **When the job waits for a handoff**, `download` opens the page in the browser and shows it, says what to do there, and asks for the link. The pasted link goes through `ILinkDownloads` with the command's folder and name, as if the command were run with it. So the source checks it, and it reaches the waiting job.
- **When the link isn't usable**, the command explains and asks again:
  - a line that isn't a link, or a link no source knows;
  - a link the site refuses;
  - a link to another file. A job just added for that file is cancelled.
- **The job is read after the watch begins**, rather than taken from the add. A page shown before the link arrived would otherwise be taken for a refusal.
- **Ctrl+C leaves the job waiting** and says so. When the input ends without a link, the command exits with code 3. The same command run again shows the page again.
- `download "nxm://…" --to <folder>` finishes a job waiting in that folder, or downloads the file outright.
- **Looking a link up can now need an account**, so a refused account and an unknown mod or file get messages of their own.
- **The pasted link is never written back out.**

## Consequences

- With a personal API key linked, the CLI downloads Nexus files for Premium accounts directly, and for free accounts through the website and a pasted link.
- Copying an `nxm://` link by hand is awkward. The browser has to show the link it couldn't open, and another mod manager registered for `nxm://` takes the link instead. That's acceptable for the CLI, which is a testing host; Phase 6 registers ModShelf as the handler, on opt-in, and passes links to the running app.
- **Changing the meaning of `ResolveAsync`'s handoff** means changing every source and the contract tests together. `SourceContractTests` passes the link to the resolver for its own links that complete a handoff.
- **Fixed on the way:** cancelling a download whose folder didn't exist yet failed, because deleting its partial file threw. When that happened while a download ran, whoever asked for the cancel waited for good. Now there's nothing to delete, and a failure to delete reaches the caller.

## Open

- **Which status Nexus Mods gives an expired or foreign key.** The recording script's `-NxmLink` option records a free account's download link for a fresh key. Trying a link again after it expires would show the refusal, and the refused statuses here can then narrow.
