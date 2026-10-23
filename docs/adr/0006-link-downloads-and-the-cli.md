# 6. Link downloads, the direct-URL source and the CLI

- Status: Accepted
- Date: 2026-10-23

## Context

Phase 1 ends with the CLI driving the download engine end to end through a direct-URL source, with no mod site involved: `download <url> --to <folder>` and `queue list`. The hosts may reference the source projects for DI registration only, so the CLI can't build a source's file reference itself. Later, the desktop app's "paste a link" and Nexus Mods page links need the same step: turning a URL into a file reference that some source can resolve.

## Decision

### Recognizing links

`Sources.Abstractions` gains a capability, `ILinkRecognizer.RecognizeAsync(link)`, which returns a `RecognizedLink` (the file, a suggested file name and whether the match is a catch-all) or null. `Application` gains `ILinkDownloads.AddAsync(link, folder, name)`, which asks every recognizer and queues the file:

- A site-specific match wins over a catch-all one. Two site-specific matches are a configuration error.
- No match throws `UnsupportedLinkException`.
- An unfinished job (anything but Completed or Cancelled) for the same file, folder and name is returned instead of adding a duplicate. Re-adding a link therefore continues the earlier download rather than starting a second one on the same `.part` file.

### The direct-URL source

`ModShelf.Sources.DirectUrl` (source ID `direct`) resolves and recognizes plain http and https links, as a catch-all.

- The link is the file's ID, because nothing else identifies the file. It's stored with the job, which lets a download resume after a restart. It's never logged.
- Links with a user name or password are refused, because credentials must never reach the database.
- A signed link that expires can't be resumed once it has expired; after the scheduler's link refreshes run out, the job fails with LinkUnavailable.
- The expected hash can ride in a pip-style fragment: `#sha256=<hex>`, or `md5`, `sha1`, `sha512`. The fragment is stripped before the request, and a malformed or doubled hash is refused when the link is added.
- The suggested file name is the last path segment, unescaped, with any character forbidden on Windows, macOS or Linux replaced by `_` and trailing dots and spaces removed. A link to a folder suggests no name.
- Its display name comes from its own `Strings.resx`.

Every source's test project derives from `SourceContractTests<TSource>`, a shared file in `tests/Shared` that each project compiles in. It checks the documented ID, the display name, refusing another source's files, never recognizing `file:`, `javascript:` or `mailto:` links, and that recognized links resolve. Checks for a capability the source lacks are skipped.

### The CLI

Each command builds its own generic host through `CliHost`, the composition root:

- **`download`** runs the scheduler as a hosted service, watches the job, and stops the host when the job ends. Ctrl+C cancels, and stopping the host requeues the running job and keeps its `.part` file; this is the CLI's "pause". A crash leaves the job in Downloading, and the scheduler requeues it at the next start. Running the same command again finds the unfinished job, resumes or retries it if needed, and continues from the `.part` file.
- **`queue list`** starts the host without background downloads, so only the database migration runs, and reads the repository.
- **Exit codes:** 0 done, 1 failed, 2 bad input, 130 stopped with Ctrl+C.
- **Database:** `LibraryDatabaseLocation.GetDefaultPath()`, `library.db` under the user's local application data folder. Configuration key `ModShelf:DatabasePath` (environment variable `ModShelf__DatabasePath`) overrides it.
- **Logging** goes to stderr at Warning by default (`appsettings.json`), so it never mixes with command output.
- **Ctrl+C:** System.CommandLine handles it and gives the command 10 seconds to stop cleanly. A no-op host lifetime keeps the generic host's console lifetime from handling Ctrl+C as well.

The desktop host isn't wired to the engine yet. Until a single-instance lock exists, a running desktop app and CLI would both work the same queue.

### Logging fix

`IHttpClientFactory`'s built-in request logging writes each URL's host and path at Information level, which breaks the rule against logging full download URLs. The file-transfer client now removes those loggers; `HttpFileTransfer` already logs the host and file name it needs. A test checks every log category at Trace level for the URL's path and query.

## Consequences

- A new source that understands its site's links implements `ILinkRecognizer`, and it takes priority over direct links automatically.
- Downloading a file that already exists fails with a storage error rather than overwriting it, and leaves that failed job in the queue.
- The CLI's strings are plain English in code: it's a test host, and the `.resx` rule covers views and ViewModels. The source's display name, which the desktop app will show, is in `.resx`.
- The CLI has no test project of its own. Its logic is kept thin: link handling is tested in Application, and the source in its own project. The full pause, kill and resume flow was checked by hand against a local server.
