using ModShelf.Application.Diagnostics;
using ModShelf.Application.Diagnostics.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps the deprecated calls counted in memory, with each call added as it came. The test can make it fail.
/// </summary>
internal sealed class MemoryDeprecatedCallStore : IDeprecatedCallStore
{
    public List<DeprecatedCall> Calls { get; } = [];

    /// <summary>
    /// Each call added, as given.
    /// </summary>
    public List<(string Name, string Code, string? Client, DateTimeOffset At)> Added { get; } = [];

    /// <summary>
    /// What reading and counting throw, while set.
    /// </summary>
    public Exception? Failure { get; set; }

    public string Location => "memory";

    public Task<IReadOnlyList<DeprecatedCall>> LoadAsync(CancellationToken cancellationToken)
        => Failure is { } failure ? Task.FromException<IReadOnlyList<DeprecatedCall>>(failure) : Task.FromResult<IReadOnlyList<DeprecatedCall>>([.. Calls]);

    public Task AddAsync(string name, string code, string? client, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            return Task.FromException(failure);
        }

        Added.Add((name, code, client, at));
        int index = Calls.FindIndex(call => call.Name == name);
        if (index < 0)
        {
            Calls.Add(new DeprecatedCall(name, code, 1, at, at, client));
        }
        else
        {
            Calls[index] = Calls[index] with { Code = code, Count = Calls[index].Count + 1, Last = at, Client = client };
        }

        return Task.CompletedTask;
    }
}
