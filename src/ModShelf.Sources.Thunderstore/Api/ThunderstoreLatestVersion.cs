namespace ModShelf.Sources.Thunderstore.Api;

/// <summary>
/// The newest version of a <see cref="ThunderstorePackageDetail"/>, of which ModShelf reads only the number.
/// </summary>
internal sealed record ThunderstoreLatestVersion(string VersionNumber);
