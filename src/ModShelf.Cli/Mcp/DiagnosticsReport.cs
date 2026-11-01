namespace ModShelf.Cli.Mcp;

/// <summary>
/// The report a bug report needs: ModShelf's version and system, its folders, the settings, the library, the queue and
/// the linked accounts, without keys, account names or links.
/// </summary>
internal sealed record DiagnosticsReport(string Report);
