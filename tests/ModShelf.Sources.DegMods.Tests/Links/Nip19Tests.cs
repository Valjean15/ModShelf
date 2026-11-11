using ModShelf.Sources.DegMods.Links;

namespace ModShelf.Sources.DegMods.Tests.Links;

public sealed class Nip19Tests
{
    private const string Admin = "f4bf1fb5ba8be839f70c7331733e309f780822b311f63e01f9dc8abbb428f8d5";

    // DEG Mods' admin's npub, as its site writes it in a link.
    private const string AdminNpub = "npub17jl3ldd6305rnacvwvchx03snauqsg4nz8mruq0emj9thdpglr2sst825x";

    private const string Author = "16e23dd46d8f413b6a993ff5555841d1ad76df5e45476ada84593f5ed4c1e1b1";
    private const string D = "7cce3021-6514-47f7-a054-a28dc1269ff4";

    // Better Newspapers for New Vegas Bounties II's address, as nostr-tools writes it: worked out with BIP-173's reference.
    private const string Naddr = "naddr1qqjrwcmrv5enqv3395mr2vf5956rwe3h94snqdf594snywryvvcnyd3evenrgq3qzm3rm4rd3aqnk65e8l642kzp6xkhdh67g4rk4k5ytyl4a4xpuxcsxpqqqpu6vwmyaq6";

    // The same, with a relay among its parts.
    private const string NaddrWithRelay = "naddr1qqjrwcmrv5enqv3395mr2vf5956rwe3h94snqdf594snywryvvcnyd3evenrgqghwaehxw309aex2mrp0yhxget8d4hkguewvdhk6q3qzm3rm4rd3aqnk65e8l642kzp6xkhdh67g4rk4k5ytyl4a4xpuxcsxpqqqpu6vwkchvy";

    [Fact]
    public void WritesAndReadsAPublicKeyAsAnNpub()
    {
        Assert.Equal(AdminNpub, Nip19.Npub(Admin));
        Assert.True(Nip19.TryReadNpub(AdminNpub, out string key));
        Assert.Equal(Admin, key);
        Assert.True(Nip19.TryReadNpub(AdminNpub.ToUpperInvariant(), out key));
        Assert.Equal(Admin, key);
    }

    [Fact]
    public void WritesAModsAddressAsDegModsDoes()
    {
        Assert.Equal(Naddr, Nip19.Naddr(31142, Author, D));
    }

    [Theory]
    [InlineData(Naddr)]
    [InlineData(NaddrWithRelay)]
    public void ReadsAnAddressItsRelaysLeftOut(string naddr)
    {
        Assert.True(Nip19.TryReadNaddr(naddr, out (int Kind, string PublicKey, string D) address));
        Assert.Equal((31142, Author, D), address);
    }

    [Fact]
    public void ReadsBackAnAddressWithADTagOfAnyText()
    {
        string naddr = Nip19.Naddr(30402, Author, "Ünïcode name with spaces");

        Assert.True(Nip19.TryReadNaddr(naddr, out (int Kind, string PublicKey, string D) address));
        Assert.Equal((30402, Author, "Ünïcode name with spaces"), address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("npub1")]
    [InlineData(AdminNpub + "q")]
    [InlineData("nprofile1qqsrhuxx8l9ex335q7he0f09aej04zpazpl0ne2cgukyawd24mayt8gpp4mhxue69uhhytnc9e3k7mgpz4mhxue69uhkg6nzv9ejuumpv34kytnrdaksjlyr9p")]
    public void SomethingElseIsntAnNpub(string? text)
    {
        Assert.False(Nip19.TryReadNpub(text, out _));
    }

    [Theory]
    [InlineData(AdminNpub)]
    [InlineData("naddr1qqqqqqqq")]
    [InlineData("naddr1QQjrwcmrv5enqv3395mr2vf5956rwe3h94snqdf594snywryvvcnyd3evenrgq3qzm3rm4rd3aqnk65e8l642kzp6xkhdh67g4rk4k5ytyl4a4xpuxcsxpqqqpu6vwmyaq6")]
    [InlineData("naddr1qqjrwcmrv5enqv3395mr2vf5956rwe3h94snqdf594snywryvvcnyd3evenrgq3qzm3rm4rd3aqnk65e8l642kzp6xkhdh67g4rk4k5ytyl4a4xpuxcsxpqqqpu6vwmyaq7")]
    public void SomethingElseIsntAnAddress(string text)
    {
        Assert.False(Nip19.TryReadNaddr(text, out _));
    }

    [Fact]
    public void AnAddressWithoutItsKindAuthorOrDTagIsntOne()
    {
        byte[] author = Convert.FromHexString(Author);
        byte[] onlyAuthor = [2, 32, .. author];
        byte[] noD = [2, 32, .. author, 3, 4, 0, 0, 0x79, 0xa6];
        byte[] shortKind = [0, 1, (byte)'x', 2, 32, .. author, 3, 2, 0x79, 0xa6];
        byte[] cutShort = [0, 10, 1, 2];

        Assert.False(Nip19.TryReadNaddr(Bech32.Encode("naddr", onlyAuthor), out _));
        Assert.False(Nip19.TryReadNaddr(Bech32.Encode("naddr", noD), out _));
        Assert.False(Nip19.TryReadNaddr(Bech32.Encode("naddr", shortKind), out _));
        Assert.False(Nip19.TryReadNaddr(Bech32.Encode("naddr", cutShort), out _));
    }

    [Fact]
    public void AnAddressWithTwoDTagsIsItsFirsts()
    {
        byte[] author = Convert.FromHexString(Author);
        byte[] twice = [0, 1, (byte)'a', 0, 1, (byte)'b', 2, 32, .. author, 3, 4, 0, 0, 0x79, 0xa6];

        Assert.True(Nip19.TryReadNaddr(Bech32.Encode("naddr", twice), out (int Kind, string PublicKey, string D) address));
        Assert.Equal((31142, Author, "a"), address);
    }

    [Fact]
    public void TheSameDataUnderAnotherPrefixIsntAnNpubOrAnAddress()
    {
        Assert.False(Nip19.TryReadNpub(Bech32.Encode("note", Convert.FromHexString(Author)), out _));
        Assert.True(Bech32.TryDecode(Naddr, out _, out byte[] address));
        Assert.False(Nip19.TryReadNaddr(Bech32.Encode("nevent", address), out _));
    }

    [Fact]
    public void Bech32WhosePaddingIsntZerosIsntBech32()
    {
        // "npub" and 32 bytes leave a value's last bit as padding: set, with a checksum to match.
        Assert.True(Bech32.TryDecode(AdminNpub, out _, out _));

        Assert.False(Bech32.TryDecode(WithLastValueOdd(AdminNpub), out _, out _));
    }

    /// <summary>
    /// The text with the last value of its data turned odd, and its checksum made again, as BIP-173 makes it.
    /// </summary>
    private static string WithLastValueOdd(string text)
    {
        const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
        int separator = text.LastIndexOf('1');
        string prefix = text[..separator];
        List<int> values = [.. text[(separator + 1)..^6].Select(character => Charset.IndexOf(character, StringComparison.Ordinal))];
        values[^1] |= 1;
        List<int> expanded = [.. prefix.Select(character => character >> 5), 0, .. prefix.Select(character => character & 31), .. values, 0, 0, 0, 0, 0, 0];
        uint[] generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];
        uint check = 1;
        foreach (int value in expanded)
        {
            uint top = check >> 25;
            check = ((check & 0x1ffffff) << 5) ^ (uint)value;
            for (int bit = 0; bit < 5; bit++)
            {
                check ^= ((top >> bit) & 1) != 0 ? generator[bit] : 0;
            }
        }

        check ^= 1;
        return prefix + "1" + string.Concat(values.Select(value => Charset[value])) + string.Concat(Enumerable.Range(0, 6).Select(index => Charset[(int)((check >> (5 * (5 - index))) & 31)]));
    }
}
