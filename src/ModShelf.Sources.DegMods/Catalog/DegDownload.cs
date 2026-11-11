namespace ModShelf.Sources.DegMods.Catalog;

/// <summary>
/// A file of a mod, as its event lists it: where it is, its SHA-256 when the author gave one, and what the author called
/// it.
/// </summary>
/// <param name="Id">
/// The file's ID in ModShelf: the first 16 hexadecimal digits of its SHA-256, or of its link's when it has none.
/// </param>
/// <param name="Url">Where the file is: a file server, or a page that leads to it.</param>
/// <param name="Sha256">Its SHA-256 in lowercase hexadecimal, or null.</param>
/// <param name="Title">What the author called it, such as "Main File", or null.</param>
/// <param name="FileName">The file's name as the author gave it, or null.</param>
/// <param name="Version">Its version, or null.</param>
internal sealed record DegDownload(string Id, Uri Url, string? Sha256, string? Title, string? FileName, string? Version);
