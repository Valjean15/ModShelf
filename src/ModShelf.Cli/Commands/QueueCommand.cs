using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Downloads;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>queue list</c>: shows the saved download jobs, oldest first, without starting any download.
/// </summary>
internal static class QueueCommand
{
    public static Command Create(CliEnvironment environment)
    {
        Command list = new("list", "Shows every download in the queue, oldest first. Nothing starts downloading.");
        list.SetAction((_, cancellationToken) => ListAsync(environment, cancellationToken));

        return new Command("queue", "Shows the download queue.") { list };
    }

    private static async Task<int> ListAsync(CliEnvironment environment, CancellationToken cancellationToken)
    {
        // Without background downloads, starting the host only creates or migrates the database.
        using IHost host = environment.BuildHost(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<DownloadJob> jobs = await host.Services.GetRequiredService<IDownloadJobRepository>()
                .ListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (jobs.Count == 0)
            {
                environment.Output.WriteLine("The queue is empty.");
                return ExitCodes.Done;
            }

            string[][] rows =
            [
                ["ID", "Status", "Progress", "File", "Folder"],
                .. jobs.Select(job => new[]
                {
                    job.Id.ToString()[..8],
                    job.Status.ToString(),
                    job.Status == DownloadJobStatus.Completed ? "-" : Sizes.Progress(job.BytesDownloaded, job.TotalBytes),
                    job.FileName,
                    job.TargetDirectory,
                }),
            ];
            TextTable.Write(environment.Output, rows);

            return ExitCodes.Done;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
