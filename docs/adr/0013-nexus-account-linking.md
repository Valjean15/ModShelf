# 13. Nexus Mods account linking

- Status: Accepted
- Date: 2026-10-26

## Context

Every request to the Nexus Mods API needs an account, and so does every download. Nexus Mods offers two ways for an app to act for a user:

- **A personal API key**, which the user copies from their account settings and the API takes in an `apikey` header. The [API acceptable use policy](https://help.nexusmods.com/article/114-api-acceptable-use-policy) tolerates it while an app is being tested.
- **Signing in through the browser**: OAuth 2.0 at `users.nexusmods.com`, whose [discovery document](https://users.nexusmods.com/.well-known/openid-configuration) lists the authorization, token, user info and revocation endpoints, the scopes `openid` and `public`, and PKCE with S256. The API takes the access token as a bearer token. It needs a client ID, which Nexus Mods issues once ModShelf is registered. That hasn't happened yet.

`IAccountLinker.GetProfileAsync` looked up whose account some credentials belong to without being told which way they were obtained. With two ways that send credentials differently, the source couldn't know whether to send an `apikey` header or a bearer token.

## Decision

### Telling the source how credentials were obtained

- **`GetProfileAsync` takes the `AuthKind`** the credentials came from, one of the source's own strategies. The account service passes the kind of the strategy it just linked with.
- A source refuses a kind it doesn't offer, rather than sending credentials a way it doesn't know. Every source's shared contract tests check it, along with offering each kind at most once.

### The Nexus source as an account linker

- **`WorksWithoutAccount` is false.**
- **Whose account it is comes from `v1/users/validate.json`**, sent the way the credentials' kind sends them. From the answer:
  - `name` becomes the display name;
  - `profile_url` becomes the avatar, when it's an absolute HTTP or HTTPS address;
  - the level is Premium if `is_premium`, otherwise Supporter if `is_supporter`, otherwise Free.

  The answer also holds the key itself and the account's email. Neither is read, so neither is ever kept.
- **A refused key or token** comes back as `AuthenticationFailedException`, through the API client (ADR 0012).

### The personal API key

- Offered always. The user is sent to `https://next.nexusmods.com/settings/api-keys`, where Nexus Mods shows the key.
- Sent in the `apikey` header, replacing any one already there.
- Checked when the source looks up the profile, so a wrong key links nothing.
- Never refreshed: a key the API refuses has to be linked again.

### Signing in through the browser

- **Offered first, and only when `NexusOptions.OAuthClientId` is set.** A client ID is never committed. The hosts read it from configuration.
- **The sign-in page** is `users.nexusmods.com/oauth/authorize`. It's requested with:
  - `response_type=code`, the client ID and the app's loopback redirect;
  - `scope=openid public`;
  - a PKCE challenge (S256) over a fresh 32-byte verifier;
  - a fresh state, which must come back unchanged.
- **The code is exchanged at `/oauth/token`** with its verifier and the client ID, and no client secret.
- **Tokens:**
  - The access token goes to the API as a bearer token, and any `apikey` header is removed.
  - Its expiry comes from `expires_in`.
  - Refreshing uses the refresh token, and keeps it when the answer doesn't bring a new one.
- **Refusals:** a 400 or 401 from the token endpoint is a refusal. During sign-in it becomes `AuthenticationFailedException`; during a refresh it means signing in again. Other failures come back as `HttpRequestException`.
- **The sign-in requests go through the source's client too**, so they carry the app's headers and retries. The app leaves the credentials on them alone while it links or refreshes (ADR 0010).

## Open until Nexus Mods issues a client ID

These can only be checked against the real sign-in server. Its answers in the tests are written by hand from the OAuth standard and the discovery document. Once a client ID exists, the recording script should record a real sign-in and refresh, and any differences get fixed here.

- **Client secret.** The discovery document lists only `client_secret_basic` and `client_secret_post` for the token endpoint. A desktop app can't keep a secret, so whether ModShelf gets a public client without one needs asking at registration.
- **Redirect.** The redirect is `http://127.0.0.1:<any free port>/`, as RFC 8252 recommends for desktop apps. Nexus Mods may require the exact address it registers, port included. If so, the loopback listener needs a fixed port option.
- **Profile lookup.** It's assumed that `validate.json` takes the bearer token, which other mod managers report. If it doesn't, the user info endpoint has the name, avatar and `membership_roles`.
- **Token lifetime, refresh token rotation, and revoking tokens on unlink.** The revocation endpoint exists, but `IAuthStrategy` has no way to revoke yet.

## Consequences

- `account link nexus` works today with a personal API key, and offers the browser first once a client ID is configured, with no further code change.
- A second source with several ways to link, such as mod.io's API key and emailed code, gets the right kind in `GetProfileAsync` without a change to Application.
