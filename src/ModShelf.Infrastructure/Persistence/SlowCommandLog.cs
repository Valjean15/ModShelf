using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Persistence;

/// <summary>
/// Warns of a library database command that takes <see cref="DefaultThreshold"/> or more, as one waiting for another
/// connection's lock does, or that fails after as long: how long, and what it was (<see cref="CommandSummary"/>), never
/// its values. A transaction's start and commit count as commands: a change in a transaction waits there.
/// </summary>
internal sealed partial class SlowCommandLog : DbCommandInterceptor, IDbTransactionInterceptor
{
    /// <summary>
    /// How long a command takes before it's logged.
    /// </summary>
    public static readonly TimeSpan DefaultThreshold = TimeSpan.FromSeconds(1);

    private readonly ILogger<SlowCommandLog> _logger;
    private readonly TimeSpan _threshold;

    /// <param name="threshold">How long a command takes before it's logged; <see cref="DefaultThreshold"/> when null.</param>
    public SlowCommandLog(ILogger<SlowCommandLog> logger, TimeSpan? threshold = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _threshold = threshold ?? DefaultThreshold;
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Executed(command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Executed(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Executed(command, eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Executed(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Executed(command, eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Executed(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Failed(command, eventData);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Failed(command, eventData);
        return Task.CompletedTask;
    }

    public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Took(eventData.Duration, "BEGIN");
        return result;
    }

    public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        Took(eventData.Duration, "BEGIN");
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Took(eventData.Duration, "COMMIT");

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Took(eventData.Duration, "COMMIT");
        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => FailedAfter(eventData.Duration, eventData.Action.ToUpperInvariant());

    public Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        FailedAfter(eventData.Duration, eventData.Action.ToUpperInvariant());
        return Task.CompletedTask;
    }

    private void Executed(DbCommand command, CommandEndEventData eventData)
    {
        if (eventData.Duration >= _threshold)
        {
            Took(eventData.Duration, CommandSummary.Of(command.CommandText));
        }
    }

    private void Failed(DbCommand command, CommandEndEventData eventData)
    {
        if (eventData.Duration >= _threshold)
        {
            FailedAfter(eventData.Duration, CommandSummary.Of(command.CommandText));
        }
    }

    private void Took(TimeSpan duration, string command)
    {
        if (duration >= _threshold)
        {
            LogSlow(_logger, duration.TotalSeconds, command);
        }
    }

    private void FailedAfter(TimeSpan duration, string command)
    {
        if (duration >= _threshold)
        {
            LogFailedSlowly(_logger, command, duration.TotalSeconds);
        }
    }
}
