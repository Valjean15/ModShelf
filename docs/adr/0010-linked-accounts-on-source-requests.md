# 10. Linked accounts on source requests

- Status: Accepted
- Date: 2026-10-23

## Context

ADR 0007 planned two pieces for Infrastructure: a table of linked accounts, and an HTTP handler that adds each source's account to its requests. ADR 0009 built the account service and its `ICredentialProvider`.

Three things had to be settled before the handler could be written:

- **How sources reach the handler.** Sources reference only Sources.Abstractions, so they can't add Infrastructure's handler to their own HTTP clients. The hosts shouldn't have to set up each source either, because adding a source is meant to need one project and one registration.
- **How credentials are added.** Each site does it differently: Nexus takes an API key in a header of its own and OAuth tokens as bearer tokens. The strategy the account was linked with knows how (`IAuthStrategy.Authorize`). But `ICredentialProvider.GetAsync` returned the credentials without saying which strategy they belong to.
- **Requests made while linking or refreshing.** A strategy talks to its site while it checks a new key or refreshes a token. If those requests went through the handler, two things would go wrong. Linking again would send the old account next to the new key. A refresh would wait for the source's lock, which the refresh itself holds, and never finish.

## Decision

### The table

- **`LinkedAccounts` holds one row per source**, keyed by the source ID. It stores how the account was linked, the display name, avatar URL and membership level, and when it was linked.
- **It holds no secrets.** The credentials stay in the OS secret store. A test checks the table's columns, so a credential column can't slip in.
- **It's added by the second migration, `AddLinkedAccounts`.** `InitialCreate` is unchanged, and a test upgrades a Phase 1 database and keeps its jobs.
- **`LinkedAccountRepository` replaces a source's earlier row on save.** `AddLibraryDatabase()` registers it.

### Source API clients

- **A source asks `IHttpClientFactory` for the client named `SourceHttpClients.NameFor(sourceId)`.** `SourceHttpClients` is a small static class in Sources.Abstractions.
- **`AddSourceHttpClients()` in Infrastructure registers a handler-builder filter.** The filter puts a `LinkedAccountHandler` first in line on every client with such a name, and on no other client. The hosts call it once; no source needs its own setup.
- **The factory's request logging stays on for these clients.** .NET 10 already hides query strings and header values in it, so keys and tokens don't reach the log.

### `ICredentialProvider.AuthorizeAsync` replaces `GetAsync`

- **`AuthorizeAsync(source, request)` adds the account to the request** with the strategy of the kind it was linked with, refreshing the credentials first when they're about to expire.
- **It returns the credentials it added,** so the handler can report them refused to `RefreshAsync`.
- **It returns null when it leaves the request alone:**
  - the source has no linked account;
  - the account has to be linked again;
  - the source no longer offers the way the account was linked (this case is logged, event 407);
  - the request belongs to linking or refreshing that source.
- Nothing used `GetAsync` on its own, so it's gone.

### The handler

1. It asks the provider to add the account and sends the request.
2. On a 401 with credentials added, it calls `RefreshAsync` once.
3. With refreshed credentials, it sends the request again. The request first gets back the headers and address it had before the first credentials were added, so the new credentials replace the old ones instead of joining them.
4. A request body is buffered before the first attempt, so it can be sent twice. API bodies are small.
5. It never sends a third time. A second 401 goes back to the caller.

When the refresh fails:
- if the site refuses it, the caller gets the original 401 and the account is marked as needing to be linked again;
- if it's a network error, the error reaches the caller.

All other responses, including 403 and 429, are left to the caller.

### Requests made while linking or refreshing

- **The account service marks the current call flow** while it links a source, which covers the strategy's calls and the profile lookup, and while it refreshes a source's credentials. It uses an `AsyncLocal` inside `AccountService`.
- **`AuthorizeAsync` leaves that flow's requests to the same source alone, without taking the source's lock.** The strategy adds the credentials it is checking itself, as `SourceHttpClients` documents. Requests to other sources, and from other flows, are handled as usual.
- **Sources need nothing extra for this.**

## Consequences

- A new source gets credentials on its requests by using its named client. Neither the hosts nor Application change.
- ADR 0009 said the handler would call `GetAsync`. `AuthorizeAsync` takes its place.
- Retries, timeouts and rate limits for source clients aren't set up yet. They come with the Nexus source in Phase 3, through the same client names.
- The hosts don't call `AddAccounts()`, `AddPlatformServices()` or `AddSourceHttpClients()` yet. The CLI gets them with its account commands in step 5.
