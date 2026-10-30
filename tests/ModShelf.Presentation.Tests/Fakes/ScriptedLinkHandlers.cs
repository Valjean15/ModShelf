using ModShelf.Application.Links;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// The sites' links as the test sets them, and what taking or giving them back makes of them, recording each call.
/// </summary>
internal sealed class ScriptedLinkHandlers : ILinkHandlers
{
    public List<LinkHandler> Handlers { get; } = [];

    /// <summary>
    /// Each call so far, such as "Take nexus".
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    /// What the next call throws instead.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// The state a site's links are in after taking them.
    /// </summary>
    public Func<SchemeState, SchemeState> AfterTake { get; set; } = state => new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: state.OtherApp);

    /// <summary>
    /// The state a site's links are in after giving them back.
    /// </summary>
    public Func<SchemeState, SchemeState> AfterGiveBack { get; set; }
        = state => state.PreviousApp is { } previous ? new SchemeState(SchemeHandlerKind.OtherApp, previous) : SchemeState.None;

    public Task<IReadOnlyList<LinkHandler>> ListAsync(CancellationToken cancellationToken)
    {
        Calls.Add("List");
        return Refused<IReadOnlyList<LinkHandler>>() ?? Task.FromResult<IReadOnlyList<LinkHandler>>([.. Handlers]);
    }

    public Task<LinkHandler> TakeAsync(SourceId source, CancellationToken cancellationToken)
        => Change($"Take {source}", source, AfterTake);

    public Task<LinkHandler> GiveBackAsync(SourceId source, CancellationToken cancellationToken)
        => Change($"GiveBack {source}", source, AfterGiveBack);

    private Task<LinkHandler> Change(string call, SourceId source, Func<SchemeState, SchemeState> after)
    {
        Calls.Add(call);
        if (Refused<LinkHandler>() is { } refused)
        {
            return refused;
        }

        int index = Handlers.FindIndex(handler => handler.Source == source);
        Handlers[index] = Handlers[index] with { State = after(Handlers[index].State) };
        return Task.FromResult(Handlers[index]);
    }

    private Task<T>? Refused<T>()
    {
        if (Refusal is not { } refusal)
        {
            return null;
        }

        Refusal = null;
        return Task.FromException<T>(refusal);
    }
}
