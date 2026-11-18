namespace ModShelf.Cli.Mcp;

/// <summary>
/// What the account said of a mod on its site, now.
/// </summary>
/// <param name="Endorsement">endorsed or abstained.</param>
internal sealed record EndorseResult(string Source, string Game, string ModId, string Endorsement, string Message);
