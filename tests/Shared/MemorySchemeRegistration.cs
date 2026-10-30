using ModShelf.Application.Links;
using ModShelf.Application.Links.Ports;

namespace ModShelf.Testing;

/// <summary>
/// A system's register of link schemes in memory: each scheme's state, as the test sets it, and what registering and
/// unregistering make of it. It records the calls.
/// </summary>
internal sealed class MemorySchemeRegistration : ISchemeRegistration
{
    public Dictionary<string, SchemeState> States { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Each call so far, such as "Register nxm".
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    /// What the next register or unregister throws instead.
    /// </summary>
    public Exception? Failure { get; set; }

    public Task<SchemeState> GetAsync(string scheme, CancellationToken cancellationToken)
        => Task.FromResult(States.GetValueOrDefault(scheme, SchemeState.None));

    public Task RegisterAsync(string scheme, CancellationToken cancellationToken)
    {
        Calls.Add($"Register {scheme}");
        Fail();
        SchemeState state = States.GetValueOrDefault(scheme, SchemeState.None);
        if (state.Kind == SchemeHandlerKind.Unavailable)
        {
            throw new InvalidOperationException("ModShelf can't be registered here.");
        }

        if (state.Kind != SchemeHandlerKind.ModShelf)
        {
            States[scheme] = new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: state.OtherApp);
        }

        return Task.CompletedTask;
    }

    public Task UnregisterAsync(string scheme, CancellationToken cancellationToken)
    {
        Calls.Add($"Unregister {scheme}");
        Fail();
        if (States.GetValueOrDefault(scheme, SchemeState.None) is { Kind: SchemeHandlerKind.ModShelf } state)
        {
            States[scheme] = state.PreviousApp is { } previous ? new SchemeState(SchemeHandlerKind.OtherApp, previous) : SchemeState.None;
        }

        return Task.CompletedTask;
    }

    private void Fail()
    {
        if (Failure is { } failure)
        {
            Failure = null;
            throw failure;
        }
    }
}
