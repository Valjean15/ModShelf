using ModShelf.Application.Accounts.Ports;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A secret store that only says whether the system protects it; the screens never read or write secrets themselves.
/// </summary>
internal sealed class FixedSecretStore(bool isProtectedByOs) : ISecretStore
{
    public bool IsProtectedByOs { get; } = isProtectedByOs;

    public Task<string?> ReadAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task WriteAsync(string name, string secret, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task DeleteAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
}
