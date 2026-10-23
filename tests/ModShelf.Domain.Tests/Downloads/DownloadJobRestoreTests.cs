using ModShelf.Domain.Downloads;

using static ModShelf.Domain.Downloads.DownloadJobStatus;

namespace ModShelf.Domain.Tests.Downloads;

public sealed class DownloadJobRestoreTests
{
    private static readonly DownloadJobId StoredId = new(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"));

    public static TheoryData<DownloadJobStatus> EveryStatus => new(Enum.GetValues<DownloadJobStatus>());

    public static TheoryData<DownloadJobStatus> EveryStatusButFailed
        => new(Enum.GetValues<DownloadJobStatus>().Where(status => status != Failed));

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void RestoringAJobsStateGivesAnEqualJob(DownloadJobStatus status)
    {
        DownloadJob original = DownloadJobs.In(status);

        DownloadJob restored = DownloadJob.Restore(
            original.Id,
            original.File,
            original.TargetDirectory,
            original.FileName,
            original.CreatedAt,
            original.Status,
            original.BytesDownloaded,
            original.TotalBytes,
            original.FailureReason);

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.File, restored.File);
        Assert.Equal(original.TargetDirectory, restored.TargetDirectory);
        Assert.Equal(original.FileName, restored.FileName);
        Assert.Equal(original.CreatedAt, restored.CreatedAt);
        Assert.Equal(original.Status, restored.Status);
        Assert.Equal(original.BytesDownloaded, restored.BytesDownloaded);
        Assert.Equal(original.TotalBytes, restored.TotalBytes);
        Assert.Equal(original.FailureReason, restored.FailureReason);
    }

    [Fact]
    public void RestoredDownloadContinuesFromItsProgress()
    {
        DownloadJob job = Restore(Downloading, bytesDownloaded: 400, totalBytes: 1000);

        job.ReportProgress(1000);
        job.StartVerifying();
        job.Complete();

        Assert.Equal(StoredId, job.Id);
        Assert.Equal(Completed, job.Status);
        Assert.Equal(1000, job.BytesDownloaded);
    }

    [Fact]
    public void RestoredFailedJobCanBeRetried()
    {
        DownloadJob job = Restore(Failed, failureReason: DownloadFailureReason.Network);

        job.Retry();

        Assert.Equal(Queued, job.Status);
        Assert.Null(job.FailureReason);
    }

    [Fact]
    public void RestoreAcceptsAnyByteCountWhenTheSizeIsUnknown()
    {
        DownloadJob job = Restore(Paused, bytesDownloaded: 5_000_000, totalBytes: null);

        Assert.Equal(5_000_000, job.BytesDownloaded);
        Assert.Null(job.TotalBytes);
    }

    [Fact]
    public void RestoreRejectsAnUnknownStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Restore((DownloadJobStatus)99));
    }

    [Fact]
    public void RestoreRejectsAnUnknownFailureReason()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Restore(Failed, failureReason: (DownloadFailureReason)99));
    }

    [Fact]
    public void RestoreRejectsAFailedJobWithoutAReason()
    {
        Assert.Throws<ArgumentException>(() => Restore(Failed, failureReason: null));
    }

    [Theory]
    [MemberData(nameof(EveryStatusButFailed))]
    public void RestoreRejectsAReasonOnAJobThatIsNotFailed(DownloadJobStatus status)
    {
        Assert.Throws<ArgumentException>(() => Restore(status, failureReason: DownloadFailureReason.Network));
    }

    [Theory]
    [InlineData(-1L, 1000L)]
    [InlineData(-1L, null)]
    [InlineData(1001L, 1000L)]
    public void RestoreRejectsAByteCountOutsideTheFile(long bytesDownloaded, long? totalBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Restore(Downloading, bytesDownloaded: bytesDownloaded, totalBytes: totalBytes));
    }

    [Fact]
    public void RestoreAppliesTheSameDestinationChecksAsCreate()
    {
        Assert.Throws<ArgumentException>(() => Restore(Queued, targetDirectory: "Downloads"));
        Assert.Throws<ArgumentException>(() => Restore(Queued, fileName: "mods/SkyUI.7z"));
        Assert.Throws<ArgumentException>(() => Restore(Queued, fileName: ".."));
    }

    [Fact]
    public void RestoreRejectsAMissingIdOrFile()
    {
        Assert.Throws<ArgumentNullException>(() => DownloadJob.Restore(
            null!, TestData.SkyUiMainFile, TestData.TargetDirectory, DownloadJobs.FileName, TestData.CreatedAt, Queued, 0, null, null));
        Assert.Throws<ArgumentNullException>(() => DownloadJob.Restore(
            StoredId, null!, TestData.TargetDirectory, DownloadJobs.FileName, TestData.CreatedAt, Queued, 0, null, null));
    }

    private static DownloadJob Restore(
        DownloadJobStatus status,
        long bytesDownloaded = 0,
        long? totalBytes = null,
        DownloadFailureReason? failureReason = null,
        string? targetDirectory = null,
        string fileName = DownloadJobs.FileName)
        => DownloadJob.Restore(
            StoredId,
            TestData.SkyUiMainFile,
            targetDirectory ?? TestData.TargetDirectory,
            fileName,
            TestData.CreatedAt,
            status,
            bytesDownloaded,
            totalBytes,
            failureReason);
}
