namespace ModShelf.Application.Tests.Common;

internal static class SchedulerTimeouts
{
    /// <summary>
    /// How long a test waits for the scheduler before failing. Generous, because it only matters when
    /// something is wrong.
    /// </summary>
    public static TimeSpan Wait { get; } = TimeSpan.FromSeconds(10);
}
