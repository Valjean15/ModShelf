namespace ModShelf.Application.Releases;

/// <summary>
/// A release of ModShelf itself, such as a newer beta on GitHub.
/// </summary>
/// <param name="Version">Such as <c>0.9.1</c>.</param>
/// <param name="Notes">What changed in it, in Markdown, when the release says.</param>
public sealed record AppRelease(string Version, string? Notes);
