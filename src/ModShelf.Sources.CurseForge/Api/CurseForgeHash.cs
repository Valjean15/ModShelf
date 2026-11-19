namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// A file's checksum.
/// </summary>
/// <param name="Algo">1 SHA-1, 2 MD5.</param>
internal sealed record CurseForgeHash(string? Value, int Algo);
