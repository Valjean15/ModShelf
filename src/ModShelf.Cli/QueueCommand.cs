using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;

namespace ModShelf.Cli;

/// <summary>
/// <c>queue list</c>: shows the saved download jobs, oldest first, without starting any download.
/// </summary>
internal static class QueueCommand
{
    private const string Gap = "  ";

    public static Command Create()
    {
        Command list = new("list", "Shows every download in the queue, oldest first. Nothing starts downloading.");
        list.SetAction((_, cancellationToken) => ListAsync(cancellationToken));

        return new Command("queue", "Shows the download queue.") { list };
    }

    private static async Task<int> ListAsync(CancellationToken cancellationToken)
    {
        // Without background downloads, starting the host only creates or migrates the database.
        using IHost host = CliHost.Build(runDownloads: false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<DownloadJob> jobs = await host.Services.GetRequiredService<IDownloadJobRepository>()
                .ListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (jobs.Count == 0)
            {
                Console.WriteLine("The queue is empty.");
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
            int[] widths = [.. Enumerable.Range(0, rows[0].Length).Select(column => rows.Max(row => row[column].Length))];
            foreach (string[] row in rows)
            {
                Console.WriteLine(string.Join(Gap, row.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd());
            }

            return ExitCodes.Done;
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
