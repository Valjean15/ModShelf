using System.Text.Json;

using ModShelf.Sources.DegMods.Api;

namespace ModShelf.Sources.DegMods.Tests.Api;

public sealed class NostrEventTests
{
    private const string Key = "16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1";

    private static readonly string Id = new('a', 64);
    private static readonly string Sig = new('b', 128);

    [Fact]
    public void ReadsAnEventAsNip01ShapesIt()
    {
        NostrEvent? read = Read($$"""{"id":"{{Id}}","pubkey":"{{Key}}","created_at":1700000000,"kind":31142,"tags":[["d","x"],["t","a","b"],["t","c"]],"content":"text","sig":"{{Sig}}"}""");

        Assert.NotNull(read);
        Assert.Equal((Id, Key, 1_700_000_000L, 31142, "text", Sig), (read.Id, read.PubKey, read.CreatedAt, read.Kind, read.Content, read.Sig));
        Assert.Equal("x", read.D);
        Assert.Equal("a", read.Tag("t"));
        Assert.Null(read.Tag("missing"));
        Assert.Equal(["a", "b", "c"], read.Values("t"));
        Assert.Equal(["a", "c"], read.FirstValues("t"));
    }

    [Fact]
    public void AnEventWithoutADTagHasAnEmptyOne()
    {
        NostrEvent? read = Read($$"""{"id":"{{Id}}","pubkey":"{{Key}}","created_at":1,"kind":1,"tags":[["d"]],"content":"","sig":"{{Sig}}"}""");

        Assert.Equal(string.Empty, read!.D);
    }

    [Theory]
    [InlineData("""{"id":"AA","pubkey":"K","created_at":1,"kind":1,"tags":[],"content":"","sig":"S"}""")]
    [InlineData("""{"id":"I","pubkey":"k","created_at":1,"kind":1,"tags":[],"content":"","sig":"S"}""")]
    [InlineData("""{"id":"I","pubkey":"K","created_at":1,"kind":1,"tags":[],"content":"","sig":"s"}""")]
    [InlineData("""{"id":"I","pubkey":"K","created_at":-1,"kind":1,"tags":[],"content":"","sig":"S"}""")]
    [InlineData("""{"id":"I","pubkey":"K","created_at":1.5,"kind":1,"tags":[],"content":"","sig":"S"}""")]
    [InlineData("""{"id":"I","pubkey":"K","created_at":1,"kind":-1,"tags":[],"content":"","sig":"S"}""")]
    [InlineData("""{"id":"I","pubkey":"K","created_at":1,"kind":"1","tags":[],"content":"","sig":"S"}""")]
    [InlineData("""{"id":"I","pubkey":"K","created_at":1,"kind":1,"tags":{},"content":"","sig":"S"}""")]
    [InlineData("""{"id":"I","pubkey":"K","created_at":1,"kind":1,"tags":["d"],"content":"","sig":"S"}""")]
    [InlineData("""{"id":"I","pubkey":"K","created_at":1,"kind":1,"tags":[["d",1]],"content":"","sig":"S"}""")]
    [InlineData("""{"id":"I","pubkey":"K","created_at":1,"kind":1,"tags":[],"content":1,"sig":"S"}""")]
    [InlineData("""{"id":"I","pubkey":"K","created_at":1,"kind":1,"tags":[],"sig":"S"}""")]
    [InlineData("""{"id":"I","pubkey":"K","kind":1,"tags":[],"content":"","sig":"S"}""")]
    [InlineData("""["EVENT"]""")]
    public void IsntAnEventWhenItIsntShapedAsOne(string json)
    {
        string filled = json
            .Replace("\"I\"", $"\"{Id}\"", StringComparison.Ordinal)
            .Replace("\"K\"", $"\"{Key}\"", StringComparison.Ordinal)
            .Replace("\"k\"", $"\"{Key[..63]}\"", StringComparison.Ordinal)
            .Replace("\"S\"", $"\"{Sig}\"", StringComparison.Ordinal)
            .Replace("\"s\"", $"\"{Sig.ToUpperInvariant()}\"", StringComparison.Ordinal)
            .Replace("\"AA\"", $"\"{Id.ToUpperInvariant()}\"", StringComparison.Ordinal);

        Assert.Null(Read(filled));
    }

    private static NostrEvent? Read(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return NostrEvent.Read(document.RootElement);
    }
}
