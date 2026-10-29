using ModShelf.Application.Library;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A hold on the library that tells whether it's held, and refuses when the test says.
/// </summary>
internal sealed class FakeLibraryHold : ILibraryHold
{
    public bool IsHeld { get; private set; }

    /// <summary>
    /// How many holds were taken.
    /// </summary>
    public int Holds { get; private set; }

    /// <summary>
    /// What taking a hold throws instead.
    /// </summary>
    public Exception? Refusal { get; set; }

    public Task<IDisposable> HoldAsync(CancellationToken cancellationToken)
    {
        if (Refusal is not null)
        {
            return Task.FromException<IDisposable>(Refusal);
        }

        Assert.False(IsHeld, "The library is held already.");
        IsHeld = true;
        Holds++;
        return Task.FromResult<IDisposable>(new Hold(this));
    }

    private sealed class Hold(FakeLibraryHold owner) : IDisposable
    {
        public void Dispose() => owner.IsHeld = false;
    }
}
