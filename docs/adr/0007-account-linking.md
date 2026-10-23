# 7. Account linking and credentials

- Status: Accepted
- Date: 2026-10-23

## Context

Most mod sites need an account for downloads, and each authenticates differently: Nexus Mods uses OAuth 2.0 with PKCE (or a personal API key), mod.io emails a code, Modrinth uses personal tokens. Some sources work without any account. Credentials are secrets and must never reach the SQLite database, settings files or the log. Requests to a source's API need its credentials attached, and OAuth tokens expire and must be refreshed.

## Decision

### Sources (`ModShelf.Sources.Abstractions.Accounts`)

- **`IAccountLinker`** is the capability for sources that support accounts. It lists its strategies in the order to offer them, says whether the source works without an account, and looks up the `AccountProfile` (display name, avatar, tier) for a set of credentials.
- **`IAuthStrategy`** is one way of linking: `ApiKey`, `OAuthPkce` or `EmailCode`. Linking takes two steps. `BeginAsync(AuthStart)` returns an `AuthChallenge`: what the user has to do, plus the strategy's own state, such as the PKCE verifier. `CompleteAsync(challenge, answer)` turns the answer into `AccountCredentials`. The answer is the API key, the emailed code, or the redirect URI the browser came back to. The same strategy later attaches credentials to requests (`Authorize`) and refreshes them (`RefreshAsync`, which returns null when the user has to link again).
- **`AuthenticationFailedException`** means the site refused what was entered, as opposed to a network error, so the app can tell the user to check their input instead of retrying.
- **No `None` kind**, unlike the plan. A source usable without an account says so with `WorksWithoutAccount`, or doesn't implement `IAccountLinker` at all, as direct links don't.
- **Secrets never show up in text.** `AccountCredentials` and `AuthChallenge` override `ToString` to leave out tokens and state. A challenge lives only in memory between the two steps.

### Application (`ModShelf.Application.Accounts`)

- **One account per source.** `LinkedAccount` holds the source, how it was linked, the profile and the link date. It's stored through `ILinkedAccountRepository`, with no secrets. The credentials are stored through `ISecretStore`, implemented per OS in Platform.
- **Linking is one call,** `IAccountService.LinkAsync(source, kind, prompt)`. The caller passes an `IAuthPrompt` that asks the user for what's needed (an API key, or an email address and then the code) or shows the browser step. The prompt never receives the challenge's secret state. For OAuth, the service starts an `IOAuthRedirectListener` on 127.0.0.1 with a random port, opens the authorization page through `IBrowserLauncher` and waits for the redirect. Linking again replaces the earlier account; unlinking removes the account and its credentials.
- **Requests get credentials from `ICredentialProvider`,** used by an HTTP handler on each source's client. It refreshes credentials that are about to expire, and after a 401 refreshes once unless another request already has. Refreshes of one source run one at a time.
- **`ISecretStore.IsProtectedByOs`** is false for the Linux fallback without a keyring, so Settings can warn the user.

## Consequences

- A source adds accounts by implementing `IAccountLinker` and its strategies. Hosts, Application and Presentation don't change.
- The desktop app and the CLI each implement `IAuthPrompt` in their own way, with dialogs or console questions, and share the whole linking flow.
- Several accounts per source would need account IDs throughout; it's left out until a site needs it.
- The next steps implement these ports: Platform's secret stores, redirect listener and browser launcher; Application's account service and credential provider; Infrastructure's linked-accounts table (the second migration) and the auth handler.
