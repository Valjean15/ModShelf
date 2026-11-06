namespace ModShelf.Platform.Agents;

/// <summary>
/// The command line serving the address, once started.
/// </summary>
internal interface IRunningAgentServer : IDisposable
{
    /// <summary>
    /// True once it says it serves the address; false when it ends first.
    /// </summary>
    Task<bool> Ready { get; }

    /// <summary>
    /// Its exit code, once it ends.
    /// </summary>
    Task<int> Exited { get; }

    /// <summary>
    /// Ends it, if it hasn't ended.
    /// </summary>
    void Stop();
}
