namespace ModShelf.Cli.Hosting;

/// <summary>
/// The process's standard input and output as streams, which the MCP server speaks over.
/// </summary>
internal sealed record StandardStreams(Stream Input, Stream Output);
