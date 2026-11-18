namespace ModShelf.Application.Library.Ports;

/// <summary>
/// A file's hashes, read in one pass, each as lowercase hexadecimal digits.
/// </summary>
/// <param name="Sha256">Its SHA-256, which the library keeps.</param>
/// <param name="Md5">Its MD5, which a site such as Nexus Mods finds files by.</param>
public sealed record FileHashes(string Sha256, string Md5);
