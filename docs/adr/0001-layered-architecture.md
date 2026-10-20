# 1. Layered architecture with one project per mod source

- Status: Accepted
- Date: 2026-10-20

## Context

ModShelf downloads mod files from several mod sites and tracks their updates. Nexus Mods comes first, and mod.io, Modrinth or CurseForge should follow without touching the core. Every site authenticates, searches, resolves downloads and reports updates differently, and some sites don't support every feature.

The app also has to run on Windows, macOS and Linux, keep its ViewModels testable without a UI thread, and offer a headless host for testing the download engine.

## Decision

The solution is split into layers, and every dependency points inward toward `Domain`:

| Project | May reference | Never references |
|---|---|---|
| `Domain` | Nothing | Everything else |
| `Sources.Abstractions` | `Domain` | `Application`, `Infrastructure`, `Platform`, any source |
| `Application` | `Domain`, `Sources.Abstractions` | `Infrastructure`, `Platform`, any source, Avalonia |
| `Sources.<Name>` (one per site) | `Sources.Abstractions` | `Application`, `Infrastructure`, `Platform`, other sources |
| `Infrastructure` | `Application` | `Platform`, any source, Avalonia |
| `Platform` | `Application` | `Infrastructure`, any source, Avalonia |
| `Presentation` | `Application` | `Infrastructure`, `Platform`, any source, Avalonia |
| `Desktop`, `Cli` | Everything, for DI registration only | — |

- **Sources are isolated.** Each mod site lives in its own `ModShelf.Sources.<Name>` project, which sees only `Sources.Abstractions`. A source implements only the small capability interfaces its site supports (`IModCatalog`, `IModSearch`, `IDownloadResolver`, `IUpdateFeed`, `IAccountLinker`, `IWatchlistSync`). It gets its `HttpClient` through `IHttpClientFactory`, registered by the host, so it never needs `Infrastructure`.
- **Ports live where they are used.** `Application` owns the interfaces; `Infrastructure`, `Platform` and the sources implement them.
- **Only the hosts know concrete types.** `Desktop` and `Cli` are the composition roots. No library references a host.
- **Only `Desktop` references Avalonia.** ViewModels live in `Presentation` with no UI framework reference, so they are tested like any other class.

### Enforcement

`ModShelf.Architecture.Tests` has one test per rule above, and CI runs them on every push and pull request.

The tests read the resolved dependency graph from the test project's `.deps.json`, which lists every project and package reference after MSBuild has evaluated the project files. They also check the assembly references the compiler emitted, which catches a raw DLL reference that bypasses a project reference. The test project references every project under `src` (except `Desktop`) through a glob, so a new source project is checked without editing the tests.

We chose this over NetArchTest or ArchUnitNET. Both inspect type-level dependencies, and the C# compiler drops references that no code uses yet. A forbidden `ProjectReference` therefore passes those tools until someone starts using it, which is too late for a rule meant to stop the reference itself.

## Consequences

- Adding a source means one new project plus DI registration and game mappings. If a new source needs changes in `Domain`, `Application` or `Presentation`, the abstraction is wrong, and that gets raised rather than worked around. Phase 7 measures this with a second source.
- Every source passes the same `SourceContractTests<T>`, so the app treats sources interchangeably.
- More projects means more boilerplate and slower cold builds than a single project would have. We accept that for the isolation it buys.
- The architecture tests depend on the `.deps.json` format of the .NET SDK. If it changes, the tests fail loudly rather than pass silently: one test checks that every expected project is in the graph, and each rule fails if a project it names is missing.
