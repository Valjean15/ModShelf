# 78. Polish pass

- Status: Accepted
- Date: 2026-11-06

## Context

The plan (step 18): screen by screen, light and dark, wide and narrow, with screenshots before and after, look at
consistency (spacing, empty, loading and problem states, titles), the keyboard (focus order, Enter and Escape in every
dialog and card, shortcuts shown in tooltips), accessibility (names for screen readers, contrast, nothing told by
colour alone), wording (site-neutral text), and speed with a large library (1,000 mods: startup, scrolling, quick search
and the pictures). The human had no notes from using 0.9.4 (2026-11-06), so the pass is the agent's.

## What was looked at

On Linux (Xvfb, Openbox), over the scratch library (13 mods, made up, no account linked), each of the six screens:

- dark at 1280×820, and at 680×640, the narrowest the window is likely used at with the sidebar open;
- light at 1240×780, with quick search open;
- the undo bar, the quota part of Settings › Updates, and quick search, from steps 15–17.

## What changed

- **Narrow windows.** At 680 wide, the content is about 400 units wide, and three rows squeezed what matters into a
  sliver: Browse's words box ("Words in t…") beside **Search**, **Filters** and the view switch; Browse's problem, its
  text in a narrow column beside its two buttons; and Downloads' link box beside **Download** and **Save to a folder…**.
  The Library's title and summary wrapped into four short lines beside its two buttons. Each now changes shape below
  620 units, with a container query, as the Settings screen and the Library's tables already did:
  - Browse: the words box takes the whole row, and the buttons go under it; a problem's buttons go under its text.
  - Downloads: the link box takes the whole row, and its buttons go under it.
  - Library: **Recent activity** and **Rescan** go under the title and summary.
  The moved panels take their column from a style rather than an attribute, which a container query's setter can't
  override.

## What was checked and left as it was

- **Keyboard.** Every dialog closes with Escape, its cancel button's or its own (the choice dialog), and Enter confirms
  through its default button, or, adding a game, through the game picker's field; the filter card, the game picker and
  quick search close with Escape; quick search's tooltip names Ctrl+K (⌘K on macOS), the only shortcut.
- **Screen readers.** Every button without words has a name (`AutomationProperties.Name`), checked over every view; the
  undo bar's and the quota strip's messages are read out as they change.
- **Colour.** Problems, failures, updates and the used-up quota say so in words, as well as by colour; the light and
  dark themes keep the text readable on every surface looked at.
- **Wording.** The strings name a site only as an example ("such as Nexus Mods"); everything else uses the site's own
  name and words.
- **Speed**, with 1,013 mods in the library (1,000 made up across eight games, each with a file), on this machine while
  other work ran: the window showed about 1.1 seconds later than with 13 mods (5.7 against 4.6 seconds, most of it
  .NET and Avalonia starting); quick search showed what each keystroke found at once, within the half second before
  the screenshot; keys typed while the app was still listing the library at start were lost, as nothing had the
  keyboard yet. Pictures weren't read, as no account was linked.

## Consequences

- No behaviour or settings change; layout only, in the Desktop project.
- The rest of what was looked at, such as the Updates screen saying for each update that the site needs an account to
  give its changelog, stays for a later release.

## Testing

- The full suite, as the Desktop project has no tests of its own; layout was checked by screenshots before and after.
  No deliberate bugs this time: the change is in XAML alone, which no test sees.
- By hand: `docs/manual-checklist-desktop.md` new 1.8.
