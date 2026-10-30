# 31. Less repeated code in the core and the CLI

- Status: Accepted
- Date: 2026-10-29

## Context

The second review (ADR 0030) also read the code for how it's organized and how much of it repeats. The mechanical rules held everywhere, and a clone scan found little copied word for word; what repeated was logic, written out again where it was needed, and some of the copies had drifted apart. In the core and the CLI:

- **Finding a link's source:** recognizing a link the user gives, wrapping the refusal and refusing a source that can't do what's asked, written out in five use cases; seven calls refused a named source with the same message.
- **The CLI's hosts:** sixteen commands built, started and stopped a host of their own, nine starting it where a failed start skipped the stop; seven caught Ctrl+C only to say what the guard around every command says already.
- **The two composition roots:** the CLI and the desktop app registered the same sixteen services, read the same four configuration keys, and placed ModShelf's files the same way, in two copies; where ModShelf keeps its files was worked out in three more places.
- **Paths:** whether case counts in paths was written out in four places.
- **File names:** both sources made a site's file name safe with a copy of the same code, which, unlike the library's rule, kept device names and names too long for a partial file.
- **Nexus addresses:** the website's pages and the API's paths were built in twelve places, away from the parser that reads them.
- **Changelogs:** the CLI read a mod's whole page, about five requests, for one changelog; the Updates screen asked the sources itself, around Application; each matched the version itself.

The human approved refactoring the core and the CLI as a set of patches of its own, after the robustness fixes, with changelogs going through Application (a public interface change) and the hosts' shared wiring in a linked file rather than a new project.

## Decision

- **`SourceRegistryExtensions`** (Application, internal) gains `RecognizeLinkAsync`, `RecognizeForAsync<T>` and `Require<T>`, which the lookup, the watchlists, the library and the library downloads use. Messages are unchanged.
- **`CommandHost.RunAsync`** (CLI) runs a command's work in a host that's stopped however the work ends; Ctrl+C that a command doesn't word itself is left to the guard. `download`, which runs the queue, keeps its own host. `ModLinkArgument`, `CliText` and the account commands' `StatusAsync` and `SiteProblem` hold what several commands repeated.
- **`HostServices.AddModShelf`**, in `src/Shared/Hosting` (namespace `ModShelf.Hosting`) and linked into both hosts, registers ModShelf's files and every service both hosts have, from the configuration keys it defines. Each host adds what only it runs. `docs/code-organization.md` says only composition goes there.
- **`AddLibraryFiles(databasePath, app)`** (Infrastructure) registers the database, the settings beside it and the logs folder from one path, the default one when none is given, and returns their folder. It replaces the three overloads that each worked out the default. `AddSingleInstance` (Platform) now takes that folder rather than working it out again.
- **`FilePaths`** (Domain) is the one rule for paths: the settings' and the mover's same-folder checks, the mount points of the free-space check and the single-instance pipe's name use it. The mover takes a full root through the settings service's own rule.
- **`LibraryNames.File`** names files for both sources too, still suggesting none for a name of only dots and spaces. The library's "name (file ID)" for a taken name is worked out in one place.
- **`NexusLink`** builds the addresses it reads: `PageUrl`, `HandoffPage`, `ModApiPath`, `FileApiPath`, `GamePage`, `GameApiPath`. Whether a mod can be downloaded is one rule for the catalog and search.
- **`IModLookup.GetChangesAsync(mod, version)`** gives what changed in a version: the changelog's entry for exactly that label, empty when it says nothing, null when the mod's source can't describe its mods. Each mod's changelog is read once for every caller and kept while the app runs; a read that failed is tried again. `library updates --changelog` uses it. The Updates screen moves to it with the Presentation refactoring.

## Consequences

- **Fewer requests:** `library updates --changelog` asks each mod's site once, not about five times, and covers updates the site gave no page for.
- **Names:** Browse and `mod show` list a file under the name it's saved as; a site's `aux.zip` is suggested as `_aux.zip`, and a name past 200 bytes is shortened, keeping its extension.
- **Infrastructure's and Platform's registration:** a host that called `AddLibraryDatabase()`, `AddSettingsFile()` or `AddLogFiles(app)` without a path calls `AddLibraryFiles` instead, and `AddSingleInstance` needs its folder.
- No message, exit code or stored data changes otherwise.

## Testing

- Every test project, before and after each commit. New tests: the registry helpers through their users; reserved and long names from both sources; `NexusLink` reading back every page it builds; `GetChangesAsync` reading each changelog once, again after a failure, and nothing for a source without a catalog; `AddLibraryFiles` with and without a path.
- The desktop app started against a fresh database, with its services validated as it builds, until it found no display.
