namespace ModShelf.Cli.Mcp;

/// <summary>
/// A setting after a change, and what it does now, in a sentence.
/// </summary>
internal sealed record SettingResult(string Setting, string Message);
