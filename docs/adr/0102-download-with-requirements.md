# 102. Download with requirements

## Context

Since 0.9.3, a mod's page has shown what the mod needs, where its site lists it (`IModRequirements`: Nexus Mods' v2
GraphQL `ModRequirements`, and mod.io's dependencies): other mods of the site, opening on their pages in ModShelf, and
things off it, such as a script extender, opening in the browser. Getting those mods still meant opening each one and downloading its file by
hand, and the command line and agents couldn't see them at all.

The human approved, for 1.0.9 (feature 5 of the plan), a way to download a mod with the mods it needs, in the desktop
app, the command line and the MCP server; and the new Application use case, `IRequirementDownloads`.

ModShelf never installs mods (CLAUDE.md): it downloads the files into the library, and that's all.

## Decision

### Planning

`IRequirementDownloads.PlanAsync` reads what a mod needs from its site and decides, for each requirement, what
downloading it would do, from the library and the queue alone:

- **In the library:** the library has files of the mod. Being watched only doesn't count, since there's nothing on disk.
- **Downloading:** a download of one of its files is in the queue, neither finished, cancelled nor failed.
- **To download:** otherwise, its newest main file with a page, as Browse's **Download selected** takes a mod's file.
  The site is asked for the mod's files only here, one request a mod the library lacks.
- **Can't download:** the mod has no main file, or its files couldn't be listed; why is said in a sentence.
- **Elsewhere:** a thing off the site. It's only pointed to, with its page.

**Deeper** ("Include their requirements too", `--deep`, `deep`) reads what the mods to download need in turn, level by
level, down to three levels. Only mods to download are read deeper: a mod in the library or downloading is taken to have
what it needs. Each mod is listed once, where it's needed first, and never the mod itself; things off the site are
listed once by name and page. A deeper level that can't be read is logged and left out; the first level that can't be
read is the plan's error.

The plan is read breadth first, so a mod needed at two levels is listed at the shallower one, and then put in the order
of a tree: each requirement followed by what it needs, the site's order kept within each. Every front end shows that
order.

A link can be planned too (`PlanAsync(Uri, …)`), recognized as Download recognizes it, for the command line and the MCP
server, which start from links.

### Queueing

`IRequirementDownloads.QueueAsync` queues each chosen requirement's file by its page link through `ILibraryDownloads`,
as Download queues a link, so it goes where downloads go, is recorded, and its mod watched. One that can't be queued
doesn't stop the others; the result says why.

- **Desktop app:** each file's **Download** on a mod's page has a menu beside it with **Download with requirements…**,
  and the page's Requirements card has **Download what's missing…**. The dialog lists the plan, the mods to download
  ticked, the rest marked with why, and things off the site with **Open page**. Ticks are kept as the depth changes.
  **Download** asks first when a site's requests look too few, as **Download selected** does, then queues the mod's
  file first (when it came from a file's menu) and the ticked mods through the library's batch, as Browse does.
- **Command line:** `download <link> --with-requirements [--deep] [--yes]` prints the plan; without `--yes` it stops
  there, with exit code 0, so nothing downloads unasked. With `--yes`, the mods to download are queued first, the file
  after, then the file is followed, and each requirement after it, as one download is. It's into the library only:
  `--to` is refused, as are `--deep` and `--yes` without `--with-requirements`.
- **MCP server:** `download` takes `withRequirements` and `deep`. The links' own files are queued first, then each
  link's mod is planned here and its mods to download sent to the queue as links are (to the desktop app when it runs),
  each mod once across the links. The result's `requirements` says, per link, what its mod needs and what became of each.
  The tool's description says to use it only when the user asks for what the mods need.

### Not done

- **Nexus Mods' file-level requirements** aren't used: the plan works by mod, and takes each mod's newest main file.
  Nexus Mods lists some requirements per file, but its v2 API's shape for them couldn't be confirmed against
  documentation or recorded answers, so they wait until Nexus Mods says how to read them.
- **No version matching:** a requirement's newest main file is taken, whatever version the mod needs.
- **No uninstalled-requirement warnings** elsewhere in the app: requirements are read when asked, never in the
  background.

## Consequences

- A mod and the mods it needs come into the library in one step, wherever ModShelf is used, with what's off the site
  pointed to rather than downloaded.
- Planning costs one request a mod read for what it needs, and one for each mod the library lacks, for its files.
- New public types, approved: `IRequirementDownloads` and its records (`RequirementsPlan`, `PlannedRequirement`,
  `RequirementState`, `RequirementDownloads`, `QueuedRequirement`, `RequirementProblem`) in Application. No change to
  `Sources.Abstractions`, no schema change, no new package.
- `BrowseViewModel`, `LibraryViewModel` and `ModPageServices` take `IRequirementDownloads`; `ModFileViewModel` takes an
  optional callback for its menu. `IDialogService` gains `DownloadRequirementsAsync`.
- The MCP `download` tool gains two arguments and its result `requirements`, null unless asked.
- Log events 2500–2502 are new, in Application's block for requirements; 986 in Presentation's.

## Testing

- Application: every state from the library, the queue and the site; three levels and no more, each mod once, in tree
  order, a mod needed twice listed where it's needed first; a deeper level that fails; a site that lists nothing;
  queueing only what's chosen, and a refusal that doesn't stop the others.
- Presentation: the menu and the card only when the mod needs other mods; the dialog's rows and states; ticks kept
  across depths; asking first when requests look too few; what was queued and what couldn't be.
- Command line, against a site that lists requirements: listing only, downloading with `--yes`, `--deep`, a mod in the
  library left out, a mod that needs nothing, and the refusals.
- MCP server: requirements downloaded and listed, deeper, what the library has or another link gets left out, a link
  that couldn't be downloaded, the argument only when asked, and `deep` refused alone.
- By hand: the command line's section 21, the desktop app's section 23 and the MCP server's section 11, with a Stardew
  Valley Content Patcher pack.
