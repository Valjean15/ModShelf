# 46. The new look

- Status: Accepted
- Date: 2026-11-02

## Context

0.9.3 reworks the desktop app (the 0.9.2 plan's Part 2). Step 6 drew the screens as mockups on a design canvas; the
human approved the look and asked for it on the whole app, so the canvas gained every screen, the first-run setup and
the dialogs. The look: quiet, dark or light, ModShelf's blue and Inter; one main button per screen, the others plain or
quiet, and a row's other actions in a "…" menu; problems said once in a calm strip instead of red lines.

The plan's step 7a puts that look on every screen before any new feature, with what each screen has today: games first,
Settings by category, pictures and the rest come with their own steps.

## Decision

- **Colours** in `Styles/Theme.axaml`, light and dark, as `ResourceDictionary.ThemeDictionaries`, so the theme switch
  (ADR 0028) and the system's mode change them at once:
  - the `Shelf*` brushes the screens use: background, sidebar, surface, raised, line, text, muted, faint, accent, accent
    text, soft accent, warning and soft warning, good and soft good; text on each passes 4.5:1;
  - ModShelf's blue, `#1F6FEB` as in its icon, as the system accent colour with its lighter and darker shades, so check
    boxes, radio buttons and progress bars use it;
  - Fluent's own keys for buttons, accent buttons, text boxes, combo boxes and the expander, so its controls match
    without new templates; corners of 8 (controls) and 10 (overlays).
- **Shared styles** in `Styles/Shell.axaml`: the window's background and text; headings; notes in the muted colour
  rather than faded; problems in the warning colour; `Border.card`, `Border.list` (a list's frame, its rows divided by
  lines), `Border.strip` (what went wrong, or `info`); `ListBox.rows` and `ListBox.stack` (cards one above the other);
  pills (`Border.tag`, plain, `accent` or `good`); buttons plain by default, `accent` for the main action, `quiet` for
  the least, `more` for the "…" menu; the sidebar's rows; dialogs, with their title inside them as well.
- **Screens**, with the same commands and bindings:
  - the sidebar gains ModShelf's icon and name, and is 216 wide, 56 collapsed;
  - Library: each mod a card; its page, folder, watching, rescan and removal in its "…" menu; updates as a pill;
    hashes in a monospaced font; Delete quiet;
  - Updates: Check now beside the heading; what the last check couldn't check in one strip; the updates in a list's
    frame, Changes quiet and Download plain, a version's changes on a raised background;
  - Downloads: Clear finished beside the heading; a row's buttons beside its name, so what it says below has the
    row's whole width; Open page the main button of a download waiting for the user;
  - Browse: the problem in a strip with its buttons; results and each section's files in a list's frame; what's in
    the library in green;
  - Accounts and Settings: cards up to a comfortable width; problems in strips; folder patterns in a monospaced font,
    their names' column as wide as the longest up to 160;
  - the first-run setup on the surface colour, its pages between lines, Skip quiet;
  - dialogs: the title inside, then what they say in the muted colour, Cancel before the main button.
- **One new string:** `MoreActions`, the "…" button's tooltip and accessible name.
- Nothing else changes: no ViewModel, setting or database change.

## Consequences

- The app looks like the canvas, light and dark, and the steps after it change structure rather than looks.
- Actions in a "…" menu are a click further away; the main ones (Download, Check now, Rescan the library) stay
  buttons.
- In the smallest window, with the sidebar open, a folder pattern's row on the Settings screen is cramped, as it was;
  step 12 rebuilds Settings by category.

## Testing

- No Desktop test project: the screens were captured on Xvfb over a seeded library in dark and light, before and after,
  and compared with the canvas: Library, Updates, Browse, Downloads, Accounts, Settings, a mod's "…" menu and files,
  the remove question, the API key question and the first-run setup's three pages; and Library, Updates, Downloads
  and Settings at the smallest window size.
- The ViewModel tests run the same commands as before; the full `dotnet test` passes.
- By hand: `docs/manual-checklist-desktop.md` names the "…" menu where an action moved.
