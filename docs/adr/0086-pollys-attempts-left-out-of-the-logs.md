# 86. Polly's attempts left out of the logs

- Status: Accepted
- Date: 2026-11-09

## Context

The HTTP clients retry through Microsoft.Extensions.Http.Resilience, which is Polly. Polly logs each attempt of each
request at Information, under the category `Polly`: "Execution attempt. Source: 'ModShelf.Source.nexus/(null)/Retry',
… Result: '200', Handled: 'False', Attempt: '0', Execution Time: 140.2ms". The desktop app's settings write Information
up for everything but `Microsoft` and `System`, and the command line's files do too, so every request to a site, and
every picture, added a line: in the human's logs (2026-11-09), most of what was written.

Polly logs an attempt it handles, one about to be retried, as a warning, and ModShelf's own messages say what a
request was for and how it ended.

## Decision

- **`AddLogFiles`** keeps `Polly` to warnings and errors: a rule for every provider, and one for the files, whose own
  rules come from the configuration (the command line's `Logging:File`), and which a rule for every provider wouldn't
  override.
- In code rather than in `appsettings.json` and `appsettings.cli.json`: both hosts register the files there, and a
  settings file's `Default` can't bring the lines back.

## Consequences

- The logs keep Polly's retries and failures, ModShelf's own messages, and nothing for each request that went well.
- The settings files can't bring every attempt back: the rule is added after theirs, and wins over one as specific.
  Debugging that needs them changes the rule in a build of its own.

## Testing

- With the configuration as the hosts have it, Information up for the files and the rest, Polly's Information isn't
  written, its warning is, and ModShelf's own Information is.
- 3 deliberate bugs, all caught.
