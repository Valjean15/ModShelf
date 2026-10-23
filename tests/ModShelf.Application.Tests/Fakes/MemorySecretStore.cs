using System.Collections.Concurrent;

using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps secrets in memory. A name in <see cref="Damaged"/> reads as a file that can't be decrypted.
/// </summary>
internal sealed class MemorySecretStore : ISecretStore
{
    public ConcurrentDictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);

    public ConcurrentBag<string> Damaged { get; } = [];

    public bool IsProtectedByOs => true;

    public Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
        => Damaged.Contains(name)
            ? throw new InvalidDataException($"The secret '{name}' can't be decrypted.")
            : Task.FromResult(Secrets.TryGetValue(name, out string? secret) ? secret : null);

    public Task WriteAsync(string name, string secret, CancellationToken cancellationToken)
    {
        Secrets[name] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        Secrets.TryRemove(name, out _);
        return Task.CompletedTask;
    }
}
