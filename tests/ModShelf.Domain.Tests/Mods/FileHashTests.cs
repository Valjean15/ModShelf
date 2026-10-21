using System.Security.Cryptography;

using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Tests.Mods;

public sealed class FileHashTests
{
    [Theory]
    [InlineData(FileHashAlgorithm.Md5, 32)]
    [InlineData(FileHashAlgorithm.Sha1, 40)]
    [InlineData(FileHashAlgorithm.Sha256, 64)]
    [InlineData(FileHashAlgorithm.Sha512, 128)]
    public void AcceptsADigestOfTheAlgorithmsLength(FileHashAlgorithm algorithm, int hexDigits)
    {
        string value = new('a', hexDigits);

        FileHash hash = new(algorithm, value);

        Assert.Equal(algorithm, hash.Algorithm);
        Assert.Equal(value, hash.Value);
    }

    [Theory]
    [InlineData(FileHashAlgorithm.Md5, 32)]
    [InlineData(FileHashAlgorithm.Sha1, 40)]
    [InlineData(FileHashAlgorithm.Sha256, 64)]
    [InlineData(FileHashAlgorithm.Sha512, 128)]
    public void RejectsADigestOfAnyOtherLength(FileHashAlgorithm algorithm, int hexDigits)
    {
        Assert.Throws<ArgumentException>(() => new FileHash(algorithm, new string('a', hexDigits - 1)));
        Assert.Throws<ArgumentException>(() => new FileHash(algorithm, new string('a', hexDigits + 1)));
    }

    [Fact]
    public void RejectsCharactersThatAreNotHexadecimal()
    {
        Assert.Throws<ArgumentException>(() => new FileHash(FileHashAlgorithm.Md5, "g123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public void RejectsAnUnknownAlgorithm()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileHash((FileHashAlgorithm)99, TestData.Md5));
    }

    [Fact]
    public void LetterCaseDoesNotMatter()
    {
        FileHash upper = new(FileHashAlgorithm.Md5, "0123456789ABCDEF0123456789ABCDEF");

        Assert.Equal(new FileHash(FileHashAlgorithm.Md5, TestData.Md5), upper);
        Assert.Equal(TestData.Md5, upper.Value);
    }

    [Fact]
    public void FromDigestMatchesTheKnownHexadecimalForm()
    {
        // SHA-256 test vector for "abc" from FIPS 180-2.
        FileHash hash = FileHash.FromDigest(FileHashAlgorithm.Sha256, SHA256.HashData("abc"u8));

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash.Value);
    }

    [Fact]
    public void ToStringShowsTheAlgorithmAndDigest()
    {
        Assert.Equal($"Md5:{TestData.Md5}", new FileHash(FileHashAlgorithm.Md5, TestData.Md5).ToString());
    }
}
