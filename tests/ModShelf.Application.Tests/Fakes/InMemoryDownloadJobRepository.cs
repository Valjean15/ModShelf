using ModShelf.Application.Downloads.Ports;
using ModShelf.Domain.Downloads;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps jobs in memory and records the status of every save.
/// </summary>
internal sealed class InMemoryDownloadJobRepository : IDownloadJobRepository
{
    private readonly Lock _gate = new();
    private readonly List<DownloadJob> _jobs;
    private readonly List<(DownloadJobId Id, DownloadJobStatus Status)> _saves = [];

    public InMemoryDownloadJobRepository(IEnumerable<DownloadJob> storedJobs)
    {
        _jobs = [.. storedJobs];
    }

    /// <summary>
    /// The IDs of the jobs deleted, in order.
    /// </summary>
    public List<DownloadJobId> Deleted { get; } = [];

    public Task AddAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _jobs.Add(job);
            _saves.Add((job.Id, job.Status));
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _saves.Add((job.Id, job.Status));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DownloadJob>> ListAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<DownloadJob>>([.. _jobs]);
        }
    }

    public Task DeleteAsync(IReadOnlyCollection<DownloadJobId> ids, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _jobs.RemoveAll(job => ids.Contains(job.Id));
            Deleted.AddRange(ids);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The statuses saved for a job, in order.
    /// </summary>
    public IReadOnlyList<DownloadJobStatus> SavedStatuses(DownloadJobId id)
    {
        lock (_gate)
        {
            return [.. _saves.Where(save => save.Id == id).Select(save => save.Status)];
        }
    }
}
