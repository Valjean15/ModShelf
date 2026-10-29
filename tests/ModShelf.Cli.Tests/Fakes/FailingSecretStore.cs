using System.Collections.Concurrent;

using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A keyring that's there but fails while <see cref="Failing"/> is set, as one that's locked or doesn't answer does.
/// Otherwise it keeps secrets in memory.
/// </summary>
internal sealed class FailingSecretStore : ISecretStore
{
    /// <summary>
    /// What each call says while the keyring fails.
    /// </summary>
    public const string Problem = "The Secret Service couldn't do that: Timeout was reached";

    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public bool Failing { get; set; } = true;

    public bool IsProtectedByOs => true;

    public Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
        => Failing
            ? Task.FromException<string?>(new InvalidOperationException(Problem))
            : Task.FromResult(_secrets.TryGetValue(name, out string? secret) ? secret : null);

    public Task WriteAsync(string name, string secret, CancellationToken cancellationToken)
    {
        if (Failing)
        {
            return Task.FromException(new InvalidOperationException(Problem));
        }

        _secrets[name] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        if (Failing)
        {
            return Task.FromException(new InvalidOperationException(Problem));
        }

        _secrets.TryRemove(name, out _);
        return Task.CompletedTask;
    }
}
