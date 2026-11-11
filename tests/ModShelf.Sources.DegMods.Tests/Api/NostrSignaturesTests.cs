using ModShelf.Sources.DegMods.Api;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests.Api;

public sealed class NostrSignaturesTests
{
    public static TheoryData<string> Relays => ["brs.degmods.com", "relay.degmods.com"];

    [Theory]
    [MemberData(nameof(Relays))]
    public void EveryRecordedEventIsWhatItsAuthorSigned(string relay)
    {
        IReadOnlyList<NostrEvent> events = RecordedDegMods.RecordedEvents(relay);

        Assert.NotEmpty(events);
        Assert.All(events, nostrEvent => Assert.True(NostrSignatures.IsGenuine(nostrEvent), nostrEvent.Id));
    }

    [Fact]
    public void AnEventThatSaysSomethingElseIsntGenuine()
    {
        NostrEvent signed = Recorded();

        Assert.False(NostrSignatures.IsGenuine(Copy(signed, content: signed.Content + " ")));
        Assert.False(NostrSignatures.IsGenuine(Copy(signed, createdAt: signed.CreatedAt + 1)));
        Assert.False(NostrSignatures.IsGenuine(Copy(signed, tags: [.. signed.Tags.Skip(1)])));
    }

    [Fact]
    public void AnEventWhoseIdIsItsHashButSignedByAnotherIsntGenuine()
    {
        NostrEvent signed = new TestAuthor(7).Sign(1, 1_700_000_000, [["t", "x"]], "hello");
        NostrEvent other = new TestAuthor(8).Sign(1, 1_700_000_000, [["t", "x"]], "hello");

        Assert.True(NostrSignatures.IsGenuine(signed));
        Assert.False(NostrSignatures.IsGenuine(Copy(signed, sig: other.Sig)));
        Assert.False(NostrSignatures.IsGenuine(Copy(signed, pubKey: other.PubKey)));
    }

    [Fact]
    public void AnEventSignedButCarryingAnotherIdIsntGenuine()
    {
        NostrEvent signed = new TestAuthor(7).Sign(1, 1_700_000_000, [], "hello");
        NostrEvent renamed = new(new string('a', 64), signed.PubKey, signed.CreatedAt, signed.Kind, signed.Tags, signed.Content, signed.Sig);

        Assert.False(NostrSignatures.IsGenuine(renamed));
    }

    [Fact]
    public void AnEventWithAKeyOrSignatureThatIsntOneIsntGenuine()
    {
        NostrEvent signed = new TestAuthor(7).Sign(1, 1_700_000_000, [], "hello");

        // Not a point of the curve, and a signature out of range.
        Assert.False(NostrSignatures.IsGenuine(Copy(signed, pubKey: new string('f', 64))));
        Assert.False(NostrSignatures.IsGenuine(Copy(signed, sig: new string('f', 128))));
    }

    [Fact]
    public void AnEventsIdIsTheHashOfItsJsonWithOnlyWhatMustBeEscaped()
    {
        // As NIP-01 serializes it: what JSON.stringify writes, worked out with Python's json.dumps(ensure_ascii=False).
        NostrEvent nostrEvent = new(
            new string('0', 64),
            "16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1",
            1_700_000_000,
            1,
            [["t", "a\"b\\c"], ["x", "\u0001\u001f\b\t\n\f\r é 😀 \u2028 \u007f"]],
            "line\nnext \"quoted\" \\ é",
            new string('0', 128));

        Assert.Equal("0b6afcd9e2d88d9880855ba952e1ed4bca3533722e706c5542988c9e48c89027", Convert.ToHexStringLower(NostrSignatures.ComputeId(nostrEvent)));
    }

    private static NostrEvent Recorded() => RecordedDegMods.RecordedEvents("brs.degmods.com")[0];

    private static NostrEvent Copy(
        NostrEvent from,
        string? pubKey = null,
        long? createdAt = null,
        IReadOnlyList<IReadOnlyList<string>>? tags = null,
        string? content = null,
        string? sig = null)
        => new(from.Id, pubKey ?? from.PubKey, createdAt ?? from.CreatedAt, from.Kind, tags ?? from.Tags, content ?? from.Content, sig ?? from.Sig);
}
