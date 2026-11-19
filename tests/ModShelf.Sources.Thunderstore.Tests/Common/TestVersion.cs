namespace ModShelf.Sources.Thunderstore.Tests.Common;

/// <summary>
/// A version of a <see cref="TestPackage"/>, as a listing's chunk gives it.
/// </summary>
internal sealed record TestVersion(string Number, DateTimeOffset UploadedAt, long Size = 1000, long Downloads = 10);
