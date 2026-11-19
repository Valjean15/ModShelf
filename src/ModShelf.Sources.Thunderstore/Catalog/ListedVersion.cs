namespace ModShelf.Sources.Thunderstore.Catalog;

/// <summary>
/// A version of a package, as its community's listing gives it, kept small: what ModShelf shows of each file.
/// </summary>
/// <param name="Number">The version number, such as <c>1.4.66</c>: the file's ID.</param>
/// <param name="Size">The zip's size in bytes.</param>
internal sealed record ListedVersion(string Number, DateTimeOffset UploadedAt, long Size, long Downloads);
