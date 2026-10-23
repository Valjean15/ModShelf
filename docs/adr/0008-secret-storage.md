# 8. Secret storage, OAuth redirects and the browser

- Status: Accepted
- Date: 2026-10-23

## Context

Account credentials (ADR 0007) must live in the operating system's protected storage, never in the library database, settings files or the log. Each OS has its own: DPAPI on Windows, the Keychain on macOS, and on Linux the Secret Service (GNOME Keyring, KWallet), which isn't always running. OAuth on the desktop also needs a loopback redirect listener and a way to open the browser.

## Decision

### Secret stores (`ModShelf.Platform.Secrets`)

`SecretStores.ForThisSystem` picks the `ISecretStore`:

- **Windows: `DpapiSecretStore`.** One file per secret in `%LOCALAPPDATA%\ModShelf\Secrets`, encrypted with DPAPI for the current user through `System.Security.Cryptography.ProtectedData`. The secret's name is mixed into the encryption, so a file copied over another secret's file doesn't decrypt.
- **macOS: `KeychainSecretStore`.** Generic passwords in the login Keychain (service `ModShelf`, account the secret's name), through the Security framework's `SecItem` functions. It uses the file-based login keychain, because the data-protection keychain needs a signed app with entitlements. As a result, an unsigned build that changes may get one "allow access" prompt.
- **Linux with a keyring: `LibSecretSecretStore`.** Items in the default collection with the schema `app.modshelf.Secret` and the attributes `scope` and `name`, through libsecret. Only libsecret's non-variadic `…v` functions are called, since .NET can't call C variadic functions portably.
- **Linux without one: `EncryptedFileSecretStore`.**
  - One AES-GCM file per secret. The key is derived with HKDF from the machine ID (`/etc/machine-id`), the user name and the secret's name.
  - Files are readable only by the user (0600, folder 0700).
  - This keeps secrets unreadable in backups, synced folders or a copied home folder, but not from programs running as the same user. So `IsProtectedByOs` is false, a warning is logged, and Settings will say so.

All stores share the name rules: lowercase ASCII letters, digits, `-` and `.`, in parts separated by `/`. Those names work as Keychain accounts and Secret Service attributes, and map one to one onto file names (`/` becomes `_`). File-based stores write to a temporary file and rename it into place. A file that can't be decrypted raises `InvalidDataException` rather than reading as missing. The native stores run their blocking calls on the thread pool, one at a time.

### OAuth redirect listener and browser

- **`LoopbackRedirectListener`** binds a `TcpListener` to 127.0.0.1 on a port the system picks (RFC 8252). It's a TCP listener rather than `HttpListener`, which needs URL reservations on Windows. It answers a GET for the redirect URI with a localized "go back to ModShelf" page and anything else with 404. Connections that stay silent for 10 seconds are dropped. The strategy checks the OAuth state of whatever arrives.
- **`SystemBrowserLauncher`** opens only absolute http and https URLs. On Windows it goes through the shell; on macOS and Linux it runs `open` or `xdg-open` with the URL as a single argument, never as a command line. It returns false when no browser or opener exists, so the prompt can show the link instead.

### Tests and CI

- **One contract for every store.** `SecretStoreContractTests` runs against every store, and each store skips where it can't run: DPAPI off Windows, the Keychain off macOS, libsecret without a Secret Service.
- **The real system stores are used.** Tests write to the actual Keychain and keyring, with a test-only service or scope, and delete what they wrote.
- **CI starts a keyring on Linux.** The Linux job installs GNOME Keyring and runs the tests in a D-Bus session with an unlocked keyring, so the libsecret store is tested there; the fallback is tested on every OS.
- **macOS is first exercised by CI's macOS runner,** since development happens on Windows and Linux.

## Consequences

- Secrets survive restarts and reinstalls of the app, and never leave the user's account in readable form, except under the documented Linux fallback.
- Credential storage has native code. A Keychain or libsecret change in a future OS release shows up as failing contract tests in CI.
- A locked or missing keyring on Linux with no way to prompt (a headless session) makes writes fail with an `InvalidOperationException` that names the reason; the store doesn't silently fall back once chosen.
