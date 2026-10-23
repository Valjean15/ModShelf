# 11. Account commands and end-to-end account tests

- Status: Accepted
- Date: 2026-10-23

## Context

Phase 2 is done when "a fake OAuth source and a fake API-key source can each be linked, survive an app restart, refresh an expired token, and unlink cleanly on all three operating systems". ADRs 0007 to 0010 built the pieces: strategies, the secret stores, the account service and the handler on each source's API client. The CLI is the only host that runs yet, so it needed account commands, and the promise needed tests that run on every CI runner.

Three things had to be settled:

- **Which secrets a test may touch.** The Keychain and the Secret Service belong to the user, and the secret files' default folder holds the user's own ModShelf credentials. A test that links an account mustn't read, overwrite or delete them.
- **How to test a command that waits for a person.** Linking asks for a key or waits for an approval in the browser, and the user can press Ctrl+C at any point. The existing CLI tests run the built CLI as a process, which can't type a hidden key or follow an OAuth redirect.
- **Where the keyring warning goes.** Without a keyring, Linux keeps secrets in files encrypted for the computer, and the secret store logs a warning. In the CLI that log line lands on stderr as a log entry, in log-speak.

## Decision

### The commands

- **`account list`** shows each source that can link an account: the account's name and level, when it was linked, and the ways to link one. A source that works without an account says so.
- **`account link <source> [--with api-key|browser|email]`** links with the first way the source offers, or the one named. An API key typed on a terminal isn't shown. A key piped in is read as a line, so scripts work.
- **`account unlink <source>`** removes the account and its credentials. It works for a source that's no longer registered, so leftovers can be cleaned up.
- **Exit codes** stay those of the other commands: 0 done, 1 refused by the site or unreachable, 2 bad input (unknown source or way, blank answer), 130 Ctrl+C. Nothing is linked unless the exit code is 0.
- **Console reads run off the command's thread** and are awaited with the cancellation token. Console reads block and ignore the token, so this is what lets Ctrl+C end a command waiting at a prompt.
- **The commands warn in their own words** when no keyring protects the credentials. The CLI's `appsettings.json` raises the secret store's log category to errors, so the same warning isn't repeated as a log entry.

### Where secrets go

- **`AddPlatformServices()` takes `SecretStoreOptions`**: the folder for secret files (Windows, and the Linux fallback) and a scope that keeps these secrets apart in the Keychain and the Secret Service. The default scope is `ModShelf`.
- **The CLI reads them from `ModShelf:SecretsFolder` and `ModShelf:SecretScope`** (`ModShelf__SecretsFolder` and `ModShelf__SecretScope` as environment variables), next to `ModShelf:DatabasePath`.
- **Only a source's API client opens the secret store.** The handler-builder filter resolves the credential provider when it sets up such a client, so a plain download never touches the keyring.

### The tests

- **Commands run against a `CliEnvironment`**: input, output, error, whether a person is at the terminal, and the function that builds each command's host. `Program.cs` passes the console and `CliHost`; tests pass writers they can read while the command runs, a reader they type into, and a token they cancel as Ctrl+C would.
- **`CliHost.Build` takes settings and a last service hook.** Tests point the database and secret files at a temporary folder, use a new `ModShelf.Tests.<guid>` scope for each test, and plug in the fakes. Everything else is what the CLI really uses.
- **Each command builds its own host**, as each run of the real CLI does, so consecutive commands see the app restart between them. The lifecycle tests also start the CLI's host without a command to have a source call its API.
- **The site is fake; the rest is real.** A small HTTP server on 127.0.0.1 implements OAuth 2.0 with PKCE and rotating refresh tokens, a personal API key, and an endpoint that says whose account a request carries. It can expire its tokens early. Two fake sources call it through their named clients, like real sources. The secret store is the system's own, the database is SQLite on disk, and OAuth goes through the real loopback listener. A scripted browser follows the site's redirect back to it.
- **What they prove**, on every CI runner and on Linux both with and without a keyring:
  - each account links, survives restarts and unlinks, leaving no row, no secret and no credential on requests;
  - an expired OAuth token is refreshed once and sent again, and the refreshed tokens are stored;
  - a token about to expire is refreshed before use;
  - no credential appears in the output, the log, or any file the app writes;
  - test secrets never reach the default folder and scope.
- **Tests clean up after themselves** by unlinking both fake sources, which deletes their secrets from the keyring. A test run that crashes can leave items under a `ModShelf.Tests.*` scope, which nothing reads.

## Consequences

- Phase 2's promise is checked on Windows, macOS and Linux by every CI run, without a live account.
- The process-based download tests stay as they are: they prove what only a separate process can, such as surviving a kill.
- A real source plugs into the same commands and the same tests' shape. Nexus in Phase 3 needs no CLI changes to be linked.
- `SourceAccountStatus` still doesn't say that an account needs linking again (ADR 0009), so `account list` can't show it yet.
