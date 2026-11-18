# Decision records

An ADR (architecture decision record) explains one significant decision: the problem, what was decided, and what
follows from it.

## Writing a new one

Add `docs/adr/NNNN-short-title.md`, numbered after the last one. Numbering continues from **0099**, so the numbers
the code's comments cite stay unique. Use the sections the earlier records used: **Context**, **Decision**,
**Consequences**, and **Testing**. When a decision changes how ModShelf works, update
[architecture.md](../architecture.md) too, so it keeps describing the app as it is.

## Since 1.0.6

| ADR | Title |
|---|---|
| 0097 | [Browse starts empty, actions on the selected mods, and game lists for hundreds of games](0097-browse-starts-empty-batch-actions-and-compact-game-lists.md) |
| 0098 | [Mod lists, and collections read as lists](0098-mod-lists-and-collections.md) |
| 0099 | [Mods that leave their site](0099-mods-that-leave-their-site.md) |

## The records of 1.0 (0001–0096)

The 96 records written while building 1.0 are summarized in [architecture.md](../architecture.md), whose
sections name the records they draw on. The full texts are kept in git history at tag `v1.0.6`. Read one on GitHub
from the links below, or locally:

```powershell
git show v1.0.6:docs/adr/0040-the-mcp-server.md
git ls-tree --name-only v1.0.6 docs/adr/        # list them all
```

| ADR | Title |
|---|---|
| 0001 | [Layered architecture with one project per mod source](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0001-layered-architecture.md) |
| 0002 | [Download job state machine](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0002-download-job-state-machine.md) |
| 0003 | [Download scheduler on a single message loop](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0003-download-scheduler.md) |
| 0004 | [Resumable HTTP file transfer](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0004-resumable-file-transfer.md) |
| 0005 | [Library database on SQLite with EF Core](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0005-library-database.md) |
| 0006 | [Link downloads, the direct-URL source and the CLI](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0006-link-downloads-and-the-cli.md) |
| 0007 | [Account linking and credentials](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0007-account-linking.md) |
| 0008 | [Secret storage, OAuth redirects and the browser](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0008-secret-storage.md) |
| 0009 | [Account service and credential refresh](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0009-account-service-and-credential-refresh.md) |
| 0010 | [Linked accounts on source requests](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0010-linked-accounts-on-source-requests.md) |
| 0011 | [Account commands and end-to-end account tests](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0011-account-commands-and-end-to-end-account-tests.md) |
| 0012 | [Nexus Mods API client](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0012-nexus-api-client.md) |
| 0013 | [Nexus Mods account linking](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0013-nexus-account-linking.md) |
| 0014 | [Handoffs and Nexus Mods downloads](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0014-handoffs-and-nexus-downloads.md) |
| 0015 | [The mod catalog](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0015-mod-catalog.md) |
| 0016 | [Mod search](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0016-mod-search.md) |
| 0017 | [Update feeds and watchlists](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0017-update-feeds-and-watchlists.md) |
| 0018 | [The library](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0018-the-library.md) |
| 0019 | [Library downloads and storage rules](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0019-library-downloads-and-storage-rules.md) |
| 0020 | [Moving the library, and old versions](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0020-moving-the-library-and-old-versions.md) |
| 0021 | [The update checker](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0021-the-update-checker.md) |
| 0022 | [Notices of new updates](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0022-notices-of-new-updates.md) |
| 0023 | [The desktop shell](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0023-the-desktop-shell.md) |
| 0024 | [The Downloads screen](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0024-the-downloads-screen.md) |
| 0025 | [The Library and Updates screens](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0025-the-library-and-updates-screens.md) |
| 0026 | [The Browse screen](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0026-the-browse-screen.md) |
| 0027 | [Hardening the CLI and the core](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0027-hardening-the-cli-and-the-core.md) |
| 0028 | [The Accounts and Settings screens](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0028-the-accounts-and-settings-screens.md) |
| 0029 | [The tray, and the end of Phase 5](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0029-the-tray-and-the-end-of-phase-5.md) |
| 0030 | [A second hardening pass](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0030-a-second-hardening-pass.md) |
| 0031 | [Less repeated code in the core and the CLI](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0031-less-repeated-code-in-the-core-and-the-cli.md) |
| 0032 | [Less repeated code in Presentation](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0032-less-repeated-code-in-presentation.md) |
| 0033 | [Less repeated markup and strings in the desktop app](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0033-less-repeated-markup-and-strings-in-the-desktop-app.md) |
| 0034 | [Update links that go back in time](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0034-update-links-that-go-back-in-time.md) |
| 0035 | [Versions and diagnostics](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0035-versions-and-diagnostics.md) |
| 0036 | [The nxm:// link handler](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0036-the-nxm-link-handler.md) |
| 0037 | [The first-run setup](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0037-the-first-run-setup.md) |
| 0038 | [Installers and self-update](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0038-installers-and-self-update.md) |
| 0039 | [The release workflow](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0039-the-release-workflow.md) |
| 0040 | [The MCP server](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0040-the-mcp-server.md) |
| 0041 | [The MCP server's first fixes](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0041-the-mcp-servers-first-fixes.md) |
| 0042 | [Forgetting a mod](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0042-forgetting-a-mod.md) |
| 0043 | [New optional files](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0043-new-optional-files.md) |
| 0044 | [File hashes](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0044-file-hashes.md) |
| 0045 | [Rescanning the library](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0045-rescanning-the-library.md) |
| 0046 | [The new look](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0046-the-new-look.md) |
| 0047 | [Pictures and saved mod details](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0047-pictures-and-saved-mod-details.md) |
| 0048 | [The mod page](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0048-the-mod-page.md) |
| 0049 | [Mods' details and requirements from Nexus Mods' GraphQL API](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0049-mod-details-and-requirements-from-nexus-graphql.md) |
| 0050 | [The Library by game](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0050-library-by-game.md) |
| 0051 | [Updates by game, and downloading them all](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0051-updates-by-game-and-batches.md) |
| 0052 | [Selecting mods, and downloading several links](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0052-selecting-mods-and-downloading-several-links.md) |
| 0053 | [Browse's toolbar and a game's rows](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0053-browse-toolbar-and-rows.md) |
| 0054 | [Settings by category, with a search](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0054-settings-by-category.md) |
| 0055 | [Each game's own settings](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0055-games-own-settings.md) |
| 0056 | [The library's history](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0056-library-history.md) |
| 0057 | [Pins, skipped updates and notes](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0057-pins-skipped-updates-and-notes.md) |
| 0058 | [Games' covers](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0058-game-covers.md) |
| 0059 | [The MCP server after a day's use](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0059-mcp-feedback-november-3.md) |
| 0060 | [Tool arguments that don't fit](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0060-tool-arguments-that-dont-fit.md) |
| 0061 | [Deprecated tool arguments](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0061-deprecated-tool-arguments.md) |
| 0062 | [The structure every source follows](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0062-the-structure-every-source-follows.md) |
| 0063 | [Games per site](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0063-games-per-site.md) |
| 0064 | [Search capabilities and each site's words](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0064-search-capabilities-and-site-words.md) |
| 0065 | [One matcher for names](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0065-one-matcher-for-names.md) |
| 0066 | [Settings and UI state](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0066-settings-and-ui-state.md) |
| 0067 | [A folder per game](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0067-a-folder-per-game.md) |
| 0068 | [CLI and MCP catch up](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0068-cli-and-mcp-catch-up.md) |
| 0069 | [Sites shown where there could be two](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0069-sites-shown-where-there-could-be-two.md) |
| 0070 | [Browse: filters in a side card](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0070-browse-filters-in-a-side-card.md) |
| 0071 | [The game picker](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0071-the-game-picker.md) |
| 0072 | [Previews and folders in Settings](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0072-previews-and-folders-in-settings.md) |
| 0073 | [The account in the menu](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0073-the-account-in-the-menu.md) |
| 0074 | [Remember the window](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0074-remember-the-window.md) |
| 0075 | [Quick search](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0075-quick-search.md) |
| 0076 | [Undo](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0076-undo.md) |
| 0077 | [Quota warning](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0077-quota-warning.md) |
| 0078 | [Polish pass](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0078-polish-pass.md) |
| 0079 | [Releasing 1.0.0](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0079-releasing-1.0.0.md) |
| 0080 | [Agent access](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0080-agent-access.md) |
| 0081 | [Saying what holds the database](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0081-saying-what-holds-the-database.md) |
| 0082 | [The database writes ahead](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0082-the-database-writes-ahead.md) |
| 0083 | [The database off the caller's thread](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0083-the-database-off-the-callers-thread.md) |
| 0084 | [A big game opens and returns quickly](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0084-a-big-game-opens-and-returns-quickly.md) |
| 0085 | [Several command line processes](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0085-several-command-line-processes.md) |
| 0086 | [Polly's attempts left out of the logs](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0086-pollys-attempts-left-out-of-the-logs.md) |
| 0087 | [GameBanana, the second site](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0087-gamebanana-source.md) |
| 0088 | [Games by name, each site apart, and a read-only GameBanana profile](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0088-games-by-name-and-a-read-only-profile.md) |
| 0089 | [mod.io, the third site](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0089-modio-source.md) |
| 0090 | [DEG Mods, the fourth site](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0090-degmods-source.md) |
| 0091 | [Files from the browser](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0091-files-from-the-browser.md) |
| 0092 | [Browse's site box](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0092-browses-site-box.md) |
| 0093 | [DEG Mods' game covers](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0093-degmods-game-covers.md) |
| 0094 | [Accounts inside Settings, and Settings at the bottom of the sidebar](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0094-accounts-inside-settings.md) |
| 0095 | [Cards in rows, built while they show](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0095-cards-in-rows.md) |
| 0096 | [The games Steam installed, matched to the sites' games](https://github.com/Valjean15/ModShelf/blob/v1.0.6/docs/adr/0096-installed-steam-games.md) |
