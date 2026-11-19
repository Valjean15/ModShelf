namespace ModShelf.Application.Contents.Ports;

/// <summary>
/// The start of a file in an archive, up to as many bytes as were asked for.
/// </summary>
/// <param name="Bytes">Its first bytes, all of them when it isn't larger than asked for.</param>
/// <param name="SizeInBytes">Its whole size once unpacked.</param>
public sealed record ArchiveBytes(ReadOnlyMemory<byte> Bytes, long SizeInBytes);
