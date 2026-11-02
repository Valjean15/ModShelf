# 49. Mods' details and requirements from Nexus Mods' GraphQL API

- Status: Accepted
- Date: 2026-11-02

## Context

Step 8a built the mod page (ADR 0048) with a place for what a mod needs, and `IModRequirements` for sources to say it,
and recorded what Nexus Mods' v2 GraphQL API answers for SMAPI and Content Patcher, which needs SMAPI. The recordings
show:

- `mods` refuses a `modId` filter without `gameId`, the game's number; `legacyModsByDomain(ids: [{gameDomain, modId}])`
  takes the game's domain name, and several mods at once. A mod it doesn't know isn't among its nodes.
- A mod's thumbnails, blurred thumbnail and category name (`category`, and `modCategory { categoryId name }`) come with
  its details, which v1 gives without thumbnails and with a category ID only.
- `modRequirements.nexusRequirements` lists the mods a mod needs, each with `modId`, `modName` and `gameId` (all
  strings, the game by number), `notes` and `url` (empty strings for none), and `externalRequirement` for things
  elsewhere. DLC are listed apart, in a type not recorded yet.
- GraphQL answers carry no rate-limit headers.

The human chose (2026-11-02): the newer API for a mod's details, with the older one as a backup, and no interface
change; DLC requirements left out for now.

## Decision

- **A mod's details:** `NexusSource.GetModAsync` asks `Catalog/ModDetails.graphql` for the mod first, and takes the
  node with its ID and game: its thumbnails, blurred thumbnail, and category (`modCategory.name`, else `category`).
  It asks v1 (`v1/games/{game}/mods/{id}.json`, the category named from the game's categories) when v2 doesn't give the
  mod: no such node, a refused query or one answered with no data, an answer it doesn't understand, or any status. A
  site that can't be reached, a timeout and a refused account aren't asked again through v1; the rate limit, once used
  up, is refused by the quota handler before v1's request is sent.
- **Refused queries:** `NexusApiClient.QueryAsync` throws a refused query, or one answered with no data, as
  `HttpRequestException` with `HttpRequestError.InvalidResponse`, so callers can tell it from a site that couldn't be
  reached; its type stays `HttpRequestException`.
- **Requirements:** `NexusSource` implements `IModRequirements` with `Catalog/ModRequirements.graphql`:
  - A mod of Nexus Mods becomes a `ModRequirement` with its `ModRef`, its game's domain name found from its number:
    known from a game read before (`v1/games/{game}.json` now reads the game's `id`, and the list of games each
    game's), or from the mod's own game, read then, or from the list of games, read once while the app runs. A game
    none of them names leaves the requirement without a way to open it.
  - Something elsewhere (`externalRequirement`) has its page when its link is an absolute web link; it's called by its
    name, or its site's. A requirement that doesn't say is a mod of Nexus Mods.
  - A mod without a name is called "Mod *id*"; a requirement with nothing to call it by is left out; empty notes are
    none. A mod Nexus Mods doesn't list needs nothing. A refused query fails, and with it the page (ADR 0048).
- **Queries** ship in the assembly from `Catalog/` as from `Search/`; `ReadQuery` takes the folder. The search and the
  details share `ToModInfo`, which keeps the caller's `ModRef`.

## Consequences

- A mod's page costs one request less (the game's categories aren't read for its category) and one more for its
  requirements. A mod v2 doesn't give costs one request more, to v1. Library mods' saved details gain their thumbnails
  as they're read again, a day after they were saved (`details.maxAge`), so their thumbnails become Nexus Mods' small
  ones, and adult mods' the site's blurred thumbnails.
- DLC requirements, and what needs a mod (`modsRequiringThisMod`), aren't shown. The requirements query takes the API's
  default page of requirements.
- No interface changed; the recordings `details-*`, `requirements-*` and `graphql-details-schema` are the human's,
  committed before this change.

## Testing

- Sources: the recorded details (SMAPI: one request, thumbnails, category), the category from either field and its own
  name first, v1 for a mod v2 doesn't list, another mod or game's node, refused or empty or misunderstood answers and a
  400, the rate limit not spent twice, an unknown mod still not found; the recorded requirements (Content Patcher needs
  SMAPI, SMAPI nothing), the game read with the page naming the requirement's game, another game's mods from the list of
  games read once (with their notes), a game none names, a game that couldn't be read read again, things elsewhere with
  and without web links, a requirement that doesn't say where it is, names from their site or ID, requirements with
  nothing to call them by, mods Nexus Mods doesn't list, a refused query, another source's mod. The client: a refused
  query is `InvalidResponse`.
- 23 deliberate bugs in the fallback, the mapping, the game numbers and the client, each caught by a test.
- By hand: `docs/manual-checklist-desktop.md` 8.2b, and 4.6's thumbnails a day later.
