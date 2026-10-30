using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Accounts that are only listed.
/// </summary>
internal sealed class ListedAccounts : IAccountService
{
    public List<SourceAccountStatus> Statuses { get; } = [];

    public Task<IReadOnlyList<SourceAccountStatus>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SourceAccountStatus>>([.. Statuses]);

    public Task<LinkedAccount> LinkAsync(SourceId source, AuthKind kind, IAuthPrompt prompt, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<LinkedAccount> CheckAsync(SourceId source, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task UnlinkAsync(SourceId source, CancellationToken cancellationToken) => throw new NotSupportedException();
}
