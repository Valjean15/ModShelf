# 92. Browse's site box

- Status: Accepted
- Date: 2026-11-11

## Context

Browse's game picker (ADR 0071) offers every site's games at once, those found by name among them (ADR 0088): with four
sites that can search (Nexus Mods, GameBanana, mod.io, DEG Mods), typing `fallout` lists each site's Fallout games, each
with its site after its name, and the sites that find their games by name are asked at every word. The human asked (2026-11-11, plan 1.0.5, step 23) for
a site filter in Browse, and chose **a site box beside the game box**: **All sites**, or one.

## Decision

- **The box** (`BrowseViewModel.Sites`, `SelectedSite`): **All sites**, then each site that can search, by name. It
  shows to the left of the game picker once two sites or more can search, as the Library's site box (ADR 0069) shows
  once its games are of two sites.
- **One site** narrows the game picker (`GamePickerViewModel.ShowSite`): the library's games and the others are only
  that site's, without its name after theirs; its hint names that site alone; and of the sites that find their games
  by name, only that one is asked (`GamesByName.FindAsync` takes the one site). **All sites** offers every site's again.
- **The game chosen follows the site.** Choosing a site while a game of another is chosen chooses the site's game the
  library has the most mods of, as Browse starts; with none, the picker opens to choose one, the game chosen before
  staying until another is, its chip saying its site. Browse starts on the site's favourite game the same way.
- **The site follows the game.** A game of another site chosen elsewhere, as quick search opens it (quick search keeps
  every site's games), moves the box to that site.
- **A site's games that couldn't be read** are said only while the box offers that site's games: with DEG Mods chosen,
  Nexus Mods' "needs your account linked" doesn't show; with all sites, the first site that failed is said, as before.
- **Remembered** between runs as state, not a setting: `UiState.BrowseSite`, the site's source ID, `browseSite` in
  `ui-state.json`; a site ModShelf no longer has is passed over, for all sites. Restoring it isn't remembered again.
  One that can't be remembered is logged (970).

## Consequences

- A site's games can be found without the others' crowding the list, and GameBanana isn't asked while another site is
  chosen.
- The Library's site box and Browse's are separate: one narrows the library's games, the other Browse's. The Library's
  isn't remembered (ADR 0069); Browse's is, as the plan said, since it's where a user who browses one site goes back to.
- No public interface of Sources.Abstractions changed; Application's `UiState` gained `BrowseSite`.

## Testing

- Presentation: the box's sites; a site narrowing the picker, its names and hint, and the site not asked; all sites back;
  the favourite game chosen with a site, or the picker opened; quick search moving the box; remembered, restored, a site
  no longer there, and one that can't be remembered; a site's games that couldn't be read said only while it's offered.
- Application: `UiState.BrowseSite` trimmed, and compared. Infrastructure: `browseSite` read and written.
