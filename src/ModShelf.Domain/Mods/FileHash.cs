namespace ModShelf.Domain.Mods;

/// <summary>
/// A file checksum: the algorithm plus the digest as hexadecimal.
/// </summary>
/// <remarks>
/// The digest is kept in lowercase, so two hashes of the same file compare equal whichever letter case
/// the source used.
/// </remarks>
public sealed record FileHash
{
    public FileHash(FileHashAlgorithm algorithm, string value)
    {
        int length = HexLength(algorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != length || !IsHex(value))
        {
            throw new ArgumentException($"A {algorithm} hash has exactly {length} hexadecimal digits.", nameof(value));
        }

        Algorithm = algorithm;
        Value = value.ToLowerInvariant();
    }

    public FileHashAlgorithm Algorithm { get; }

    public string Value { get; }

    /// <summary>
    /// Creates a hash from a computed digest, such as the output of <c>MD5.HashData</c>.
    /// </summary>
    public static FileHash FromDigest(FileHashAlgorithm algorithm, ReadOnlySpan<byte> digest)
        => new(algorithm, Convert.ToHexStringLower(digest));

    public override string ToString() => $"{Algorithm}:{Value}";

    private static int HexLength(FileHashAlgorithm algorithm) => algorithm switch
    {
        FileHashAlgorithm.Md5 => 32,
        FileHashAlgorithm.Sha1 => 40,
        FileHashAlgorithm.Sha256 => 64,
        FileHashAlgorithm.Sha512 => 128,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown hash algorithm."),
    };

    private static bool IsHex(string value)
    {
        foreach (char character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
