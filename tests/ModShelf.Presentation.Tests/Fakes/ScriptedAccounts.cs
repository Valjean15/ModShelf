using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// The accounts of the sites the test adds, linked, checked and unlinked as the test scripts, recording each call.
/// Linking and checking keep the account they return; unlinking removes it.
/// </summary>
internal sealed class ScriptedAccounts : IAccountService
{
    private readonly Lock _gate = new();
    private readonly List<SourceAccountStatus> _statuses = [];
    private readonly List<string> _calls = [];

    /// <summary>
    /// What linking does: asks through the prompt, and returns the account or throws. By default it fails the test.
    /// </summary>
    public Func<SourceId, AuthKind, IAuthPrompt, CancellationToken, Task<LinkedAccount>> Link { get; set; }
        = (_, _, _, _) => throw new InvalidOperationException("The test didn't script linking.");

    /// <summary>
    /// What checking does: returns the account as the site says it is now, or throws. By default it fails the test.
    /// </summary>
    public Func<SourceId, CancellationToken, Task<LinkedAccount>> Check { get; set; }
        = (_, _) => throw new InvalidOperationException("The test didn't script checking.");

    /// <summary>
    /// What listing throws while set.
    /// </summary>
    public Exception? ListRefusal { get; set; }

    /// <summary>
    /// What unlinking throws while set.
    /// </summary>
    public Exception? UnlinkRefusal { get; set; }

    /// <summary>
    /// The quota a source reports from now on while an account is linked there.
    /// </summary>
    public Dictionary<SourceId, ApiQuota> Quotas { get; } = [];

    /// <summary>
    /// Each call, such as "list", "link nexus ApiKey", "check nexus" or "unlink nexus", oldest first.
    /// </summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public void AddSource(SourceId source, string displayName, bool worksWithoutAccount, AuthKind[] kinds, LinkedAccount? account = null)
    {
        lock (_gate)
        {
            _statuses.Add(new SourceAccountStatus(source, displayName, worksWithoutAccount, kinds, account));
        }
    }

    /// <summary>
    /// Links, or with null unlinks, an account behind the screen's back, as the command line would.
    /// </summary>
    public void SetAccount(SourceId source, LinkedAccount? account)
    {
        lock (_gate)
        {
            int index = _statuses.FindIndex(status => status.Source == source);
            _statuses[index] = _statuses[index] with { Account = account };
        }
    }

    public Task<IReadOnlyList<SourceAccountStatus>> ListAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _calls.Add("list");
            if (ListRefusal is { } refusal)
            {
                return Task.FromException<IReadOnlyList<SourceAccountStatus>>(refusal);
            }

            return Task.FromResult<IReadOnlyList<SourceAccountStatus>>(
            [
                .. _statuses.Select(status => status with { Quota = status.Account is null ? null : Quotas.GetValueOrDefault(status.Source) }),
            ]);
        }
    }

    public async Task<LinkedAccount> LinkAsync(SourceId source, AuthKind kind, IAuthPrompt prompt, CancellationToken cancellationToken)
    {
        Record($"link {source} {kind}");
        LinkedAccount account = await Link(source, kind, prompt, cancellationToken);
        SetAccount(source, account);
        return account;
    }

    public async Task<LinkedAccount> CheckAsync(SourceId source, CancellationToken cancellationToken)
    {
        Record($"check {source}");
        LinkedAccount account = await Check(source, cancellationToken);
        SetAccount(source, account);
        return account;
    }

    public Task UnlinkAsync(SourceId source, CancellationToken cancellationToken)
    {
        Record($"unlink {source}");
        if (UnlinkRefusal is { } refusal)
        {
            return Task.FromException(refusal);
        }

        SetAccount(source, null);
        return Task.CompletedTask;
    }

    private void Record(string call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }
}
