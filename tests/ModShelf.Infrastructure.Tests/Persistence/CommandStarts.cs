using System.Collections.Concurrent;
using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ModShelf.Infrastructure.Tests.Persistence;

/// <summary>
/// Says when the library database starts a command or a transaction, so a test can wait until one is under way: on a
/// busy runner, a change started on another thread can begin its commands well after the test started it.
/// </summary>
internal sealed class CommandStarts : DbCommandInterceptor, IDbTransactionInterceptor
{
    /// <summary>
    /// What a transaction's start is called, as <c>SlowCommandLog</c> calls it.
    /// </summary>
    public const string Begin = "BEGIN";

    private readonly ConcurrentQueue<(string What, TaskCompletionSource Started)> _awaited = new();

    /// <summary>
    /// Completes when the next command whose text has <paramref name="what"/> starts, or the next transaction for
    /// <see cref="Begin"/>.
    /// </summary>
    public Task NextAsync(string what)
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _awaited.Enqueue((what, started));
        return started.Task;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Starting(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Starting(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Starting(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Starting(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Starting(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Starting(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
    {
        Starting(Begin);
        return result;
    }

    public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        Starting(Begin);
        return ValueTask.FromResult(result);
    }

    private void Starting(string text)
    {
        foreach ((string what, TaskCompletionSource started) in _awaited)
        {
            if (!started.Task.IsCompleted && text.Contains(what, StringComparison.Ordinal))
            {
                started.TrySetResult();
            }
        }
    }
}
