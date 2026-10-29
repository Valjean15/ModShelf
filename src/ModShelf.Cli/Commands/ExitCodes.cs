namespace ModShelf.Cli.Commands;

/// <summary>
/// The process exit codes the commands return.
/// </summary>
internal static class ExitCodes
{
    public const int Done = 0;

    /// <summary>What the command was asked to do failed, or was refused or cancelled; the message says why.</summary>
    public const int Failed = 1;

    /// <summary>
    /// The arguments were wrong: ones the command line can't read, such as an unknown option, or ones the command
    /// refuses, such as a link no source can download.
    /// </summary>
    public const int BadInput = 2;

    /// <summary>The download waits to be started on the site, and the input ended without the link that does it.</summary>
    public const int Waiting = 3;

    /// <summary>
    /// Something went wrong that ModShelf didn't expect, such as a bug: said in a line, with the details in the log.
    /// EX_SOFTWARE, by the convention of BSD's sysexits.h.
    /// </summary>
    public const int Unexpected = 70;

    /// <summary>Stopped by Ctrl+C, by the usual convention of 128 plus SIGINT.</summary>
    public const int Interrupted = 130;
}
