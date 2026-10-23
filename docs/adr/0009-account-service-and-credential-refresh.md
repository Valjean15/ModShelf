# 9. Account service and credential refresh

- Status: Accepted; `GetAsync` replaced by `AuthorizeAsync` in [ADR 0010](0010-linked-accounts-on-source-requests.md)
- Date: 2026-10-23

## Context

ADR 0007 set out the account ports: `IAccountService` links and unlinks, and `ICredentialProvider` hands credentials to requests and refreshes them. Both read and change the same stored credentials. Several requests to one source can be refused at the same moment when its token expires, and linking again can happen while a refresh is under way. Refresh tokens can also be revoked, and networks fail.

## Decision

- **One class, two roles.** `AccountService` implements both interfaces, and `AddAccounts()` registers one instance for both. Linking, unlinking and refreshing share one in-memory copy of each source's credentials and one lock per source. A refresh can't overwrite an account that was just linked again, and refused requests can't each start their own refresh.
- **Stored as versioned JSON.** Credentials are kept in the secret store under `account/<source>`: format version, how the account was linked, access token, refresh token, expiry. They're read once per run and then served from memory. Stored credentials that can't be read are treated as no account and logged without their contents: damaged, an unknown version or kind, or a blank token. The user links again.
- **Linking order.**
  1. Credentials are obtained through the strategy and the prompt.
  2. The profile is fetched.
  3. Only then are both written: the secret first, then the account record. Both writes run to the end even if cancellation arrives in between.
  - A refused answer, a refused profile or a cancellation saves nothing.
  - For OAuth, the redirect listener is always stopped afterwards.
  - Answers are trimmed, and a blank one is rejected before reaching the site.
- **Unlinking order.** The secret goes first, then the account record. If removing the secret fails, the account stays listed and unlinking can be retried.
- **Refresh before use.** Credentials within one minute of `ExpiresAt` are refreshed before being handed out. If that refresh fails for a reason other than the site refusing it, such as a network error, the current credentials are returned unchanged: they still work for a moment, and a hiccup shouldn't block the request. API keys have no expiry and are never refreshed early.
- **Refresh after a refusal.** `RefreshAsync(rejected)` refreshes only when `rejected` is still the current credentials. Otherwise another request already refreshed them, or the account was linked again, and the current ones are returned. Here a network error reaches the caller, because the credentials just failed.
- **Linking again is required** when a refresh returns null or the site refuses it. This is remembered in memory, so later requests get null at once instead of retrying. After a restart, one more refresh is tried. Linking again clears it.
- **Logs** name sources and kinds only: never tokens, answers, codes or profiles.

## Consequences

- The auth handler (next step) only has to call `GetAsync` before a request and `RefreshAsync` once after a 401.
- `SourceAccountStatus` doesn't yet say that an account needs linking again; the UI will need that, and adding it changes a public record.
- Unlinking doesn't revoke the token on the site, because `IAuthStrategy` has no revocation; the token expires on its own or can be revoked on the site.
