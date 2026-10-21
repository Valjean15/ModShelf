namespace ModShelf.Domain.Mods;

/// <summary>
/// The checksum algorithms that sources publish for their files.
/// </summary>
/// <remarks>The numeric values are stored, so never renumber them.</remarks>
public enum FileHashAlgorithm
{
    Md5 = 1,
    Sha1 = 2,
    Sha256 = 3,
    Sha512 = 4,
}
