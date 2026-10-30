using System.Collections.Concurrent;

using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps secrets in memory. A name in <see cref="Damaged"/> reads as a file that can't be decrypted, while
/// <see cref="ReadFailure"/> is set nothing can be read, and while <see cref="WriteFailure"/> is set nothing can be stored.
/// </summary>
internal sealed class MemorySecretStore : ISecretStore
{
    public ConcurrentDictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);

    public ConcurrentBag<string> Damaged { get; } = [];

    /// <summary>
    /// What writing throws, while set, as a keyring that's locked or doesn't answer does.
    /// </summary>
    public Exception? WriteFailure { get; set; }

    /// <summary>
    /// What reading throws, while set, as a keyring that's locked does.
    /// </summary>
    public Exception? ReadFailure { get; set; }

    public bool IsProtectedByOs => true;

    public Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
        => ReadFailure is { } failure ? Task.FromException<string?>(failure)
            : Damaged.Contains(name)
            ? throw new InvalidDataException($"The secret '{name}' can't be decrypted.")
            : Task.FromResult(Secrets.TryGetValue(name, out string? secret) ? secret : null);

    public Task WriteAsync(string name, string secret, CancellationToken cancellationToken)
    {
        if (WriteFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        Secrets[name] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        Secrets.TryRemove(name, out _);
        return Task.CompletedTask;
    }
}
