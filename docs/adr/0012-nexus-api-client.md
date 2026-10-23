# 12. Nexus Mods API client

- Status: Accepted
- Date: 2026-10-23

## Context

Phase 3 adds Nexus Mods, the first real source. Every capability it implements calls the Nexus Mods API, so how it calls the API is settled first.

- **Etiquette.** The [API acceptable use policy](https://help.nexusmods.com/article/114-api-acceptable-use-policy) asks every request to name the app and its version, in `Application-Name` and `Application-Version`. A public app must be registered once it has a testing build; until then a developer's personal API key is tolerated for testing.
- **Rate limits.** Nexus Mods counts requests per account. There's a daily allowance, and once that's used up, an hourly one. Every response reports both in `X-RL-Hourly-*` and `X-RL-Daily-*` headers: the limit, what's left, and when it resets. An account that keeps going past them is refused with 429.
- **Tests.** CI never calls a live API. The tests need the API's real answers, recorded once by someone with an account and replayed.

## Decision

### The client

- **The source uses the client named `SourceHttpClients.NameFor("nexus")`**, so the app's handler adds the linked account to every request (ADR 0010). `AddNexusSource()` sets the rest of it up:
  - the base address, `https://api.nexusmods.com/`, which tests point elsewhere through `NexusOptions`;
  - `Application-Name: ModShelf` and `Application-Version`, taken from the assembly's version until releases set one (Phase 6);
  - a `ModShelf/<version>` user agent and `Accept: application/json`.
- **Retries:** 3 more attempts with exponential backoff and jitter, starting at 1 second, for server errors, 408, timeouts and broken connections. A refusal is never retried, and neither is 429: the limits reset on the hour at the earliest, and quick retries would only spend what's left.
- **Timeouts:** 30 seconds per attempt, and no overall timeout, so the retries decide how long a request can take.
- **Errors become types the rest of the app knows**, since Application can't see the source's own types:
  - 401 becomes `AuthenticationFailedException`;
  - any other refusal becomes `HttpRequestException` with the status code and the API's own message;
  - a timeout becomes `HttpRequestException` without a status code.

  Messages never hold the request's URL, because a download link request carries the `nxm://` key in its query.

### The rate limit

- **`NexusQuotaHandler` reads the `X-RL-*` headers from every response.** It sits inside the retries, so it sees each attempt, and keeps the latest values in a singleton `NexusQuota`.
- **While both allowances are used up, requests are held back** until the earlier of the two resets. The handler answers them with a 429 without sending them, so the account isn't refused over and over.
- **A 429 without headers saying the allowances are used up** holds requests back as long as its `Retry-After` says, or for an hour, which is what Nexus Mods tells users to wait.
- Holding back and being refused are logged as warnings, events 500 and 501.

### Recorded responses

- **`Fixtures/Record-NexusFixtures.ps1` records the API's answers** with the developer's own key: once for a Premium account and once for a free one, optionally with a fresh `nxm://` link for the free download. Each answer is saved with its status, its `X-RL-*` headers and its body, under `Fixtures/Recorded/<account>/`.
- **Nothing personal is kept.** The script replaces the key, the user's ID, name and email, `nxm://` keys and the tokens in download links with placeholders. It then checks every file for any of them, including any 12 characters of a key, and stops if one is left. The key comes from an environment variable or a hidden prompt, and is never written or shown.
- **The tests replay the recordings with WireMock.Net**, a test-only package. Tests that need answers no recording has, such as a server error followed by a success, set those up in WireMock directly.
- **Recordings are re-recorded, never edited by hand**, so they stay what the API really answered.

## Consequences

- Each new endpoint, such as the GraphQL search in a later step, means another recording run.
- `NexusQuota` is internal for now. Settings will show the quota, and Phase 4's update checks will plan their requests around it; each needs a way to read it then.
- Until Phase 6 sets release versions, every build reports the same `Application-Version`.
