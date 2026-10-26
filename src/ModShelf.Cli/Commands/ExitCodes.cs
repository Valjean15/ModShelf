namespace ModShelf.Cli.Commands;

/// <summary>
/// The process exit codes the commands return.
/// </summary>
internal static class ExitCodes
{
    public const int Done = 0;

    /// <summary>The download failed or was cancelled.</summary>
    public const int Failed = 1;

    /// <summary>The arguments were wrong, such as a link no source can download.</summary>
    public const int BadInput = 2;

    /// <summary>The download waits to be started on the site, and the input ended without the link that does it.</summary>
    public const int Waiting = 3;

    /// <summary>Stopped by Ctrl+C, by the usual convention of 128 plus SIGINT.</summary>
    public const int Interrupted = 130;
}
