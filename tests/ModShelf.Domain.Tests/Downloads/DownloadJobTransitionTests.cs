using ModShelf.Domain.Downloads;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Domain.Tests.Downloads;

/// <summary>
/// Checks every action against every status: the allowed ones move the job, the rest throw.
/// </summary>
public sealed class DownloadJobTransitionTests
{
    /// <summary>
    /// The expected state machine, written out independently of <see cref="DownloadJob"/>:
    /// each action, the statuses it's allowed from, and the status it leads to.
    /// </summary>
    private static readonly Dictionary<DownloadJobAction, ExpectedTransition> StateMachine = new()
    {
        [DownloadJobAction.StartResolving] = new([Queued, AwaitingHandoff, Downloading], Resolving),
        [DownloadJobAction.AwaitHandoff] = new([Resolving], AwaitingHandoff),
        [DownloadJobAction.StartDownloading] = new([Resolving], Downloading),
        [DownloadJobAction.ReportProgress] = new([Downloading], Downloading),
        [DownloadJobAction.StartVerifying] = new([Downloading], Verifying),
        [DownloadJobAction.Complete] = new([Verifying], Completed),
        [DownloadJobAction.Pause] = new([Queued, Resolving, AwaitingHandoff, Downloading], Paused),
        [DownloadJobAction.Resume] = new([Paused], Queued),
        [DownloadJobAction.Requeue] = new([Resolving, Downloading, Verifying], Queued),
        [DownloadJobAction.Fail] = new([Resolving, AwaitingHandoff, Downloading, Verifying], Failed),
        [DownloadJobAction.Retry] = new([Failed], Queued),
        [DownloadJobAction.Cancel] = new([Queued, Resolving, AwaitingHandoff, Downloading, Verifying, Paused, Failed], Cancelled),
    };

    public static TheoryData<DownloadJobAction, DownloadJobStatus> AllowedTransitions => Transitions(allowed: true);

    public static TheoryData<DownloadJobAction, DownloadJobStatus> ForbiddenTransitions => Transitions(allowed: false);

    [Fact]
    public void TheExpectedStateMachineCoversEveryAction()
    {
        Assert.Equal(Enum.GetValues<DownloadJobAction>(), StateMachine.Keys.Order());
    }

    [Theory]
    [InlineData(Completed)]
    [InlineData(Cancelled)]
    public void NoActionLeavesAFinalStatus(DownloadJobStatus final)
    {
        Assert.DoesNotContain(StateMachine.Values, transition => transition.From.Contains(final));
        Assert.True(DownloadJobs.In(final).IsFinished);
    }

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void AllowedActionMovesTheJob(DownloadJobAction action, DownloadJobStatus from)
    {
        DownloadJob job = DownloadJobs.In(from);

        Apply(job, action);

        Assert.Equal(StateMachine[action].To, job.Status);
    }

    [Theory]
    [MemberData(nameof(ForbiddenTransitions))]
    public void ForbiddenActionThrowsAndLeavesTheJobUnchanged(DownloadJobAction action, DownloadJobStatus from)
    {
        DownloadJob job = DownloadJobs.In(from);
        (DownloadJobStatus, long, long?, DownloadFailureReason?) before =
            (job.Status, job.BytesDownloaded, job.TotalBytes, job.FailureReason);

        Assert.Throws<InvalidOperationException>(() => Apply(job, action));

        Assert.Equal(before, (job.Status, job.BytesDownloaded, job.TotalBytes, job.FailureReason));
    }

    private static TheoryData<DownloadJobAction, DownloadJobStatus> Transitions(bool allowed)
    {
        TheoryData<DownloadJobAction, DownloadJobStatus> data = new();
        foreach (DownloadJobAction action in Enum.GetValues<DownloadJobAction>())
        {
            foreach (DownloadJobStatus status in Enum.GetValues<DownloadJobStatus>())
            {
                if (StateMachine[action].From.Contains(status) == allowed)
                {
                    data.Add(action, status);
                }
            }
        }

        return data;
    }

    private static void Apply(DownloadJob job, DownloadJobAction action)
    {
        switch (action)
        {
            case DownloadJobAction.StartResolving:
                job.StartResolving();
                break;
            case DownloadJobAction.AwaitHandoff:
                job.AwaitHandoff();
                break;
            case DownloadJobAction.StartDownloading:
                job.StartDownloading(0, DownloadJobs.TotalBytes);
                break;
            case DownloadJobAction.ReportProgress:
                job.ReportProgress(1);
                break;
            case DownloadJobAction.StartVerifying:
                job.StartVerifying();
                break;
            case DownloadJobAction.Complete:
                job.Complete();
                break;
            case DownloadJobAction.Pause:
                job.Pause();
                break;
            case DownloadJobAction.Resume:
                job.Resume();
                break;
            case DownloadJobAction.Requeue:
                job.Requeue();
                break;
            case DownloadJobAction.Fail:
                job.Fail(DownloadFailureReason.Network);
                break;
            case DownloadJobAction.Retry:
                job.Retry();
                break;
            case DownloadJobAction.Cancel:
                job.Cancel();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action.");
        }
    }

    private sealed record ExpectedTransition(DownloadJobStatus[] From, DownloadJobStatus To);
}
