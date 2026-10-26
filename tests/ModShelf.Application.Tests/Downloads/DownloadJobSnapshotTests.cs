using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;

namespace ModShelf.Application.Tests.Downloads;

public sealed class DownloadJobSnapshotTests
{
    private static readonly Uri Page = new("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files&file_id=35407");

    [Fact]
    public void ShowsTheHandoffPageWhileTheJobWaitsForIt()
    {
        DownloadJob job = NewJob();
        job.StartResolving();
        job.AwaitHandoff();

        Assert.Equal(Page, DownloadJobSnapshot.Of(job, Page).HandoffPage);
    }

    [Fact]
    public void LeavesTheHandoffPageOutOnceTheJobStoppedWaiting()
    {
        DownloadJob job = NewJob();
        job.StartResolving();

        Assert.Null(DownloadJobSnapshot.Of(job, Page).HandoffPage);
    }

    [Fact]
    public void CopiesTheJob()
    {
        DownloadJob job = NewJob();

        DownloadJobSnapshot snapshot = DownloadJobSnapshot.Of(job);

        Assert.Equal(
            new DownloadJobSnapshot(job.Id, job.File, job.TargetDirectory, job.FileName, DownloadJobStatus.Queued, 0, null, null, job.CreatedAt),
            snapshot);
    }

    private static DownloadJob NewJob()
        => DownloadJob.Create(TestData.File(TestData.Nexus, "35407"), TestData.TargetDirectory, "35407.7z", TestData.Now);
}
