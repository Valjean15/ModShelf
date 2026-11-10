# Adding a source

A source is how ModShelf works with one mod site. Nexus Mods (`ModShelf.Sources.Nexus`) and plain web links
(`ModShelf.Sources.DirectUrl`) are the two so far. From 1.0.1, each patch release adds one site, following this guide.
A site that doesn't fit the guide changes the guide first, before any code: see
[When the site doesn't fit](#when-the-site-doesnt-fit).

ModShelf downloads and manages files. A source never installs, extracts or deploys anything, and never takes a site
action that makes a game do so.

## 1. Research the site first

Before writing code, find out and write down, for the ADR:

- **The official API**, and its documentation. ModShelf uses official APIs only and never reads a site's web pages.
- **The terms:** whether an app must register, ask for approval or show attribution; what it may keep, and for how long.
- **The limits:** requests per minute, hour or day, per user or per app, and the headers that say what's left.
- **Accounts:** whether reading, searching or downloading needs one, and how a user links it: a personal API key, a
  sign-in through the browser (OAuth with PKCE), or a code sent by email.
- **Downloads:** whether the API gives a file's link directly, or the user has to start the download on the site, and
  how the site then hands it over (a link scheme of its own, as Nexus Mods' `nxm://`).
- **What "following" a mod does.** Some sites install what their users subscribe to: on mod.io, games built with its
  plugin download and install their players' subscriptions ([mod.io docs](https://docs.mod.io/unity/subscribing)). An
  action like that is never a watchlist for ModShelf.
- **Keys the site issues to ModShelf** rather than to the user, such as an OAuth client ID or an app's API key.

## 2. One project

A source is one project, `src/ModShelf.Sources.<Name>`, with its tests in `tests/ModShelf.Sources.<Name>.Tests`.

- It references `ModShelf.Sources.Abstractions` only: never Application, Infrastructure, Platform or another source.
  The architecture tests check this.
- Its layout follows `docs/code-organization.md`: `<Name>Source` (public, split into `<Name>Source.<Capability>.cs`
  partials), `<Name>Options` and `Add<Name>Source()` at the root; HTTP clients and DTOs in `Api/`; the site's link
  formats in `Links/`; `Resources/Strings.resx` for its display name and messages.
- It takes the next free block of log event IDs, added to the table in `docs/code-organization.md`.
- The hosts register it with one line in `src/Shared/Hosting/HostServices.cs`, reading its options from the
  configuration (`ModShelf:<Name>:…`).
- The architecture tests name it in `ProjectNames` and their list of expected projects, and reference it.

Nothing else outside the project changes. If something has to, stop and raise it: the abstraction is wrong, and the
guide is fixed first.

## 3. IDs that never change

These are stored in the library database and `settings.json`, and appear in the library's folders, so a value given
once is kept for good.

| ID | What it is | Rules |
|---|---|---|
| `SourceId` | The site, such as `nexus` | Lowercase letters, digits and inner hyphens. Documented in the source and checked by the contract tests. |
| `GameRef.Key` | A game on the site | The site's own permanent key for the game. Prefer a readable one, such as a slug the site promises to keep (`stardewvalley`), since it shows in folder names (`{game}`) and in the settings, after the site's ID (`games.nexus/stardewvalley`); otherwise its number. |
| `ModRef.Id` | A mod of that game | The site's permanent ID, not a slug its author can rename. |
| `ModFileRef.Id` | A file of that mod | The site's permanent ID. Keep it short: it goes into file names, as `SkyUI (1234).7z`, when two files share a name. |

A site whose game keys have a shape of their own, such as GameBanana's numbers, says so with
`IModCatalog.CouldBeGameKey`, so a game typed by its key alone, on the command line or by an agent, can be told to be
that site's: `fallout4` is Nexus Mods', `16951` GameBanana's. A key two sites could both have needs its site.

Games are kept apart per site. The same game on two sites is two games, each with settings of its own and its site's
name beside it; ModShelf points out that they share a name ("Also on …") and offers to copy one's settings to the other,
but never merges, links or changes them by itself.

## 4. The capabilities

`IModSource` gives the ID, the display name and the site's words (below). A source implements only the capabilities its
site has. What ModShelf does without each:

| Capability | What it is | Without it |
|---|---|---|
| `ILinkRecognizer` | Turns a link to the site's mod or file into a file reference. | Nothing of the site can be downloaded by link. |
| `IDownloadResolver` | Turns a file into a `DownloadTicket`, or `HandoffRequired` when the user must start the download on the site. | Its files can't be downloaded. |
| `IModCatalog` | The site's games, a game, a mod, its files and its changelogs. | Its files don't go into the library (they're saved to a folder, as plain web links are), and nothing is checked for updates. |
| `IModSearch` | A game's mods, a page at a time, in the orders and by the filters its `Capabilities` say. | Browse doesn't list the site's games. |
| `ITrendingMods` | What the site says is trending for a game. | Browse shows no trending row for it. |
| `IModRequirements` | What a mod needs, as its author listed it. | The mod page lists no requirements. |
| `IUpdateFeed` | Which of a game's mods changed lately, in one request. | Each watched mod is asked for its files on its own: one request per mod per check. |
| `IWatchlistSync` | The mods the account follows on the site. | Browse has no followed mods, and the mod page no button to follow. |
| `IAccountLinker` | Linking an account, with one `IAuthStrategy` per way the site allows. | No account is offered. |
| `IApiQuota` | What's left of the account's allowances, when the site says. | No quota is shown. |
| `IHandoffScheme` | The link scheme the site hands downloads over with. | ModShelf can't offer to open those links. |

A source needs `ILinkRecognizer`, `IDownloadResolver` and `IModCatalog` for its files to go into the library and be
checked for updates. The rest are optional.

### Links

- Recognize only the site's own links, and never a `file:`, `javascript:` or `mailto:` link.
- A link that's the site's but can't be downloaded as it is, such as a malformed one, throws `ArgumentException`.
- `RecognizeAsync` may ask the site, when a link doesn't name everything a reference needs: a mod's page without its
  game, or a slug in place of the mod's ID. That costs a request per link, so do it only then.
- `IsCatchAll` is for plain web links alone. `IsHandoff` marks a link that completes a handoff.

### Downloads and handoffs

- A `DownloadTicket` is short-lived and can be signed: never store, cache or log it, or the link inside it.
- Give the file's hash in the ticket when the site publishes one, in any algorithm `FileHash` takes.
- `HandoffRequired` names the page where the user starts the download. A handoff link can carry a key: it's kept in
  memory only and never logged. A source with `IHandoffScheme` makes ModShelf able to register that scheme with the
  system, which other mod managers may claim too: that's raised and asked about first.

### Files, and how updates are found

ModShelf works out updates from a mod's files as the catalog lists them, never from version labels:

- a library file's update is the newest file of its update chain (`ModFile.ReplacedBy`), when the site gives chains;
- otherwise, a main file's update is the newest **main** file uploaded after it;
- a mod with files gets the **optional** files uploaded since its last check, as new files.

A site that lists a mod's files side by side, without saying which is the main one or which replaced which, such as
variants ("pale" and "tan") or add-ons uploaded at different times, says so with `IModCatalog.FilesSideBySide`. Its
files are never taken for each other's update by date: a library file's update comes only from its chain, and the main
files uploaded since a check are new files too. GameBanana is one (ADR 0087).

So map the site's categories onto `ModFileCategory` with care:

- Each version's main download is `Main`. **A site that doesn't classify files lists each version's file as `Main`,**
  and says whether they sit side by side (above). `Unknown` is never taken as an update.
- `Update`, `Optional` and `Miscellaneous` as the site uses them; `OldVersion` and `Archived` for what the site marks
  as replaced or withdrawn. A file that isn't the mod, such as the small release file a mod manager reads, is
  `Miscellaneous`.
- `ReplacedBy` only when the site says a file replaced another, and only forward in time: leave out a link to a file
  uploaded before the one it replaces (Nexus Mods lists some; ADR 0059). A site that keeps a file's older versions
  apart without naming their successor may link each to the newer file of the same name, its version left out
  (GameBanana's archived files; ADR 0087).
- `UploadedAt` as the site says; `SizeInBytes` exact when the site gives it (a size rounded to the kilobyte still
  matches on a rescan); every hash the site publishes.
- `PageUrl` is a link the source's own recognizer takes as the file: its page, or, for a site that gives files no
  page, another link of its own to the file (GameBanana's 1-click links). Every file of a source that downloads needs
  one, since the app downloads a file it lists by it: on a mod's page, from the Updates screen, and through the
  command line and the MCP server. The contract checks it.

### The site's words

`Words` says what the site calls following a mod and approving one, from the source's own `Strings.resx`, each word
lowercase, as in the middle of a sentence; the screens put them in their own phrases and capitalize them where a
sentence starts. Nexus Mods' are:

| Word | Nexus Mods | Shown as |
|---|---|---|
| `Follow` | track | **Track on Nexus Mods** |
| `Unfollow` | untrack | **Untrack on Nexus Mods** |
| `Followed` | tracked | **Tracked mods**, "Tracked on Nexus Mods.", "12 tracked mods of Stardew Valley on Nexus Mods." |
| `Approval` | endorsement | "1 endorsement" |
| `Approvals` | endorsements | "87 endorsements", **Most endorsements**, **Fewest endorsements**, the CLI's Endorsements column |

A site with `IWatchlistSync` gives all three words for following; one without gives none. A site that counts approval
in `ModInfo.Endorsements` gives both words for it, and one that sorts by `SearchSort.Endorsements` must; a site without
gives neither, and nothing shows it: no count, no order by it, no CLI column. `SiteWords.None` is a site with neither.

### Search

`Capabilities` says what the site's search can do, and ModShelf asks nothing more: a search that would is refused before
it's sent, saying what the site can ("Example Mods can't search by author. It can search by words and by when mods were
updated."), and the screens offer only these.

- **Orders:** each `SearchOrder`, a `SearchSort` and its `SearchDirection`, the site has; its own first, which a search
  that names none uses. A sort the site has only one way round is listed only that way.
- **Filters:** what it narrows by: `Text`, `Author`, `Category`, `UpdatedSince`, `AdultContent`. A site with
  `AdultContent` leaves adult mods out unless asked. One that marks adult mods (`ModInfo.HasAdultContent`) but can't
  leave them out says so with `MarksAdultContent`: ModShelf leaves them out itself after each page, unless asked, and
  the page says how many (`SearchPage.LeftOut`), so the next one follows on. A site with neither marks no mod as adult,
  so including adult mods changes nothing there.
- **The largest page,** from 1 to 100. A page holds as many mods as asked, up to it, while more match, so pages
  follow on whole.

The source may still refuse what it can't do with `ArgumentException`; it's never sent. The contract checks that each
declared order lists the mods in it, each filter narrows them, and pages are full, against a game the test names
(`SearchableGame`).

### Accounts

- Each way of linking is an `IAuthStrategy` of one `AuthKind`: `ApiKey`, `OAuthPkce` or `EmailCode`.
  `WorksWithoutAccount` says whether the site can be used without one.
- The app keeps credentials in the system's secret store (`ISecretStore`), never in the database, settings or the log.
  The source never stores them.
- The source asks `IHttpClientFactory` for the client named `SourceHttpClients.NameFor(Id)`: the app adds the linked
  account to every request and refreshes it when it's refused. While linking, the strategy adds the credentials it's
  checking itself.

## 5. HTTP, limits and errors

The source asks `IHttpClientFactory` for the client named `SourceHttpClients.NameFor(Id)`. The hosts'
`AddSourceHttpClients()` (Infrastructure) gives that client what every site's API needs, so a source doesn't repeat it:

- the linked account on every request, refreshed once when the site refuses it;
- retries with backoff for passing failures (server errors, 408, timeouts, broken connections), but never for a
  refusal or `429`, which only spends what's left of the limit, and never waiting out a server's `Retry-After`;
- a time limit per attempt, with the whole answer read inside it; a request whose last attempt ran out is an
  `HttpRequestException` without a status, holding a `TimeoutException`;
- no redirects: followed, one would take the account's key along.

`Add<Name>Source()` sets up the rest of its client: the API's address; the headers the site asks for, such as the
app's name and version (Nexus Mods: `Application-Name` and `Application-Version`); no overall timeout
(`Timeout.InfiniteTimeSpan`), since each attempt has one; and handlers of its own, such as one that holds requests back
before the site's rate limit, from its headers. Those sit inside the retries, so they see every attempt.
`AddNexusSource()` shows it all.

- `IApiQuota` shows what's left of the limit, when the site says.
- Errors, as `SourceContractTests<T>` and the interfaces' remarks expect: `ArgumentException` for another source's game,
  mod or file, and for a search the site can't do; `AuthenticationFailedException` when the site refuses the account;
  `HttpRequestException` with its status for the rest.
- Log through `[LoggerMessage]` methods, never with a key, a token, a handoff link or a full download link.

## 6. Pictures

`GameInfo` takes a game's cover (wide) and icon (square); `ModInfo` a mod's picture, its thumbnail and, for adult mods,
a thumbnail the site blurred. All are optional web links. Without them, a game shows its initial on its colour, and a
mod shows no picture.

## 7. Keys the site issues to ModShelf

A key the site issues to ModShelf, such as an OAuth client ID, is configuration under `ModShelf:<Name>:…`: user secrets
or an environment variable while developing, and the release workflow's secrets for builds. It's never committed. Keys
tied to a personal account never leave the developer's machine.

## 8. Tests

- `tests/ModShelf.Sources.<Name>.Tests` has a class deriving from `SourceContractTests<T>`, with the source's ID, its
  own links (handoff links among them, when it has a scheme) and a factory for the source against the recordings.
  The checks of a capability the source doesn't implement are skipped. The checks of what its search can do need a
  game to search, `SearchableGame`, with more mods than a page holds, and answers to every search they make; they're
  skipped without one.
- The tests set the source up as the hosts do: `AddSourceHttpClients()`, with no account linked, then
  `Add<Name>Source()` (see `RecordedNexusApi.BuildServices`), so the retries and time limits are the app's.
- The site's answers are recorded by `Fixtures/Record-<Name>Fixtures.ps1`, which the human runs with their own
  accounts: the key comes from the environment or is asked for without being shown, personal data is replaced before a
  file is written, and every file is checked for it. The tests replay the recordings with WireMock.Net. Recordings are
  only ever changed by the script.
- Tests and CI never call the live site.
- `tests/Shared/ExampleSite` is a site shaped unlike Nexus Mods, which the tests run through the app: the contract
  (`ModShelf.Sources.Abstractions.Tests`), the CLI end to end, and the Library, Updates and Browse screens. Every
  change keeps those tests passing, so a design that works only for Nexus Mods shows there first.

## 9. What a new site adds besides the project

- An ADR, `docs/adr/NNNN-<name>-source.md`: what the research found, the capabilities taken and left, how the site's
  categories map, its limits, and what a user sees differently.
- A section in `docs/manual-checklist.md` for the human's live checks, with each kind of account the site has.
- The site in the README's list, and in the release notes of the patch release it ships in (1.0.1, 1.0.2 and on).

## When the site doesn't fit

Stop before building when the site needs:

- a change to Domain, Application or Presentation beyond registration;
- a change to a public interface in `Sources.Abstractions`;
- a new package, or a site action that installs or deploys.

Raise it with what the site does and what the change would be. Once agreed, the guide changes first, then the code.
