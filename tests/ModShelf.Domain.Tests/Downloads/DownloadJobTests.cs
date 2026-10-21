using ModShelf.Domain.Downloads;

namespace ModShelf.Domain.Tests.Downloads;

public sealed class DownloadJobTests
{
    [Fact]
    public void CreateQueuesAJobWithNoProgress()
    {
        DownloadJob job = DownloadJob.Create(
            TestData.SkyUiMainFile, TestData.TargetDirectory, DownloadJobs.FileName, TestData.CreatedAt);

        Assert.Equal(DownloadJobStatus.Queued, job.Status);
        Assert.Equal(TestData.SkyUiMainFile, job.File);
        Assert.Equal(TestData.TargetDirectory, job.TargetDirectory);
        Assert.Equal(DownloadJobs.FileName, job.FileName);
        Assert.Equal(TestData.CreatedAt, job.CreatedAt);
        Assert.Equal(0, job.BytesDownloaded);
        Assert.Null(job.TotalBytes);
        Assert.Null(job.FailureReason);
        Assert.False(job.IsFinished);
    }

    [Fact]
    public void EachJobGetsItsOwnId()
    {
        Assert.NotEqual(DownloadJobs.New().Id, DownloadJobs.New().Id);
    }

    [Fact]
    public void CreateRejectsARelativeTargetDirectory()
    {
        Assert.Throws<ArgumentException>(
            () => DownloadJob.Create(TestData.SkyUiMainFile, "Downloads", DownloadJobs.FileName, TestData.CreatedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("mods/SkyUI.7z")]
    [InlineData("SkyUI\0.7z")]
    public void CreateRejectsAFileNameThatIsNotASingleSegment(string fileName)
    {
        Assert.Throws<ArgumentException>(
            () => DownloadJob.Create(TestData.SkyUiMainFile, TestData.TargetDirectory, fileName, TestData.CreatedAt));
    }

    [Fact]
    public void CreateRejectsAMissingFileOrFileName()
    {
        Assert.Throws<ArgumentNullException>(
            () => DownloadJob.Create(null!, TestData.TargetDirectory, DownloadJobs.FileName, TestData.CreatedAt));
        Assert.Throws<ArgumentNullException>(
            () => DownloadJob.Create(TestData.SkyUiMainFile, TestData.TargetDirectory, null!, TestData.CreatedAt));
    }

    [Fact]
    public void DirectDownloadRunsToCompletion()
    {
        DownloadJob job = DownloadJobs.New();

        job.StartResolving();
        job.StartDownloading(0, 1000);
        job.ReportProgress(400);
        job.ReportProgress(1000);
        job.StartVerifying();
        job.Complete();

        Assert.Equal(DownloadJobStatus.Completed, job.Status);
        Assert.Equal(1000, job.BytesDownloaded);
        Assert.Equal(1000, job.TotalBytes);
        Assert.True(job.IsFinished);
    }

    [Fact]
    public void HandoffDownloadResolvesAgainOnceTheUserStartsIt()
    {
        DownloadJob job = DownloadJobs.New();

        job.StartResolving();
        job.AwaitHandoff();
        Assert.Equal(DownloadJobStatus.AwaitingHandoff, job.Status);

        job.StartResolving();
        job.StartDownloading(0, totalBytes: null);

        Assert.Equal(DownloadJobStatus.Downloading, job.Status);
        Assert.Null(job.TotalBytes);
    }

    [Fact]
    public void ExpiredLinkIsResolvedAgainWithoutLosingProgress()
    {
        DownloadJob job = DownloadJobs.In(DownloadJobStatus.Downloading);
        job.ReportProgress(400);

        job.StartResolving();
        Assert.Equal(400, job.BytesDownloaded);

        job.StartDownloading(400, DownloadJobs.TotalBytes);
        Assert.Equal(DownloadJobStatus.Downloading, job.Status);
        Assert.Equal(400, job.BytesDownloaded);
    }

    [Fact]
    public void PausedJobResumesWithItsProgress()
    {
        DownloadJob job = DownloadJobs.In(DownloadJobStatus.Downloading);
        job.ReportProgress(400);

        job.Pause();
        job.Resume();

        Assert.Equal(DownloadJobStatus.Queued, job.Status);
        Assert.Equal(400, job.BytesDownloaded);
    }

    [Fact]
    public void InterruptedDownloadIsRequeuedWithItsProgress()
    {
        DownloadJob job = DownloadJobs.In(DownloadJobStatus.Downloading);
        job.ReportProgress(400);

        job.Requeue();

        Assert.Equal(DownloadJobStatus.Queued, job.Status);
        Assert.Equal(400, job.BytesDownloaded);
    }

    [Theory]
    [InlineData(-1L, 1000L)]
    [InlineData(1001L, 1000L)]
    public void StartDownloadingRejectsAnOffsetOutsideTheFile(long resumeFromByte, long totalBytes)
    {
        DownloadJob job = DownloadJobs.In(DownloadJobStatus.Resolving);

        Assert.Throws<ArgumentOutOfRangeException>(() => job.StartDownloading(resumeFromByte, totalBytes));

        Assert.Equal(DownloadJobStatus.Resolving, job.Status);
    }

    [Fact]
    public void StartDownloadingAcceptsAnyOffsetWhenTheSizeIsUnknown()
    {
        DownloadJob job = DownloadJobs.In(DownloadJobStatus.Resolving);

        job.StartDownloading(5000, totalBytes: null);

        Assert.Equal(5000, job.BytesDownloaded);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(1001L)]
    public void ReportProgressRejectsAByteCountOutsideTheFile(long bytesDownloaded)
    {
        DownloadJob job = DownloadJobs.In(DownloadJobStatus.Downloading);
        job.ReportProgress(400);

        Assert.Throws<ArgumentOutOfRangeException>(() => job.ReportProgress(bytesDownloaded));

        Assert.Equal(400, job.BytesDownloaded);
    }

    [Fact]
    public void FailRecordsTheReasonAndRetryClearsIt()
    {
        DownloadJob job = DownloadJobs.In(DownloadJobStatus.Verifying);

        job.Fail(DownloadFailureReason.HashMismatch);
        Assert.Equal(DownloadJobStatus.Failed, job.Status);
        Assert.Equal(DownloadFailureReason.HashMismatch, job.FailureReason);

        job.Retry();
        Assert.Equal(DownloadJobStatus.Queued, job.Status);
        Assert.Null(job.FailureReason);
    }

    [Fact]
    public void CancellingAFailedJobClearsTheReason()
    {
        DownloadJob job = DownloadJobs.In(DownloadJobStatus.Failed);

        job.Cancel();

        Assert.Equal(DownloadJobStatus.Cancelled, job.Status);
        Assert.Null(job.FailureReason);
        Assert.True(job.IsFinished);
    }

    [Fact]
    public void FailRejectsAnUnknownReason()
    {
        DownloadJob job = DownloadJobs.In(DownloadJobStatus.Downloading);

        Assert.Throws<ArgumentOutOfRangeException>(() => job.Fail((DownloadFailureReason)999));

        Assert.Equal(DownloadJobStatus.Downloading, job.Status);
        Assert.Null(job.FailureReason);
    }

    [Fact]
    public void ForbiddenActionSaysWhatWasAttemptedAndTheStatus()
    {
        DownloadJob job = DownloadJobs.In(DownloadJobStatus.Completed);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(job.Pause);

        Assert.Contains("Pause", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Completed", exception.Message, StringComparison.Ordinal);
    }
}
