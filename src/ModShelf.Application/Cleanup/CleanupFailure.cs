namespace ModShelf.Application.Cleanup;

/// <summary>
/// A file that wasn't deleted, and why.
/// </summary>
/// <param name="Entry">The file, as it was listed.</param>
/// <param name="Message">Why, in a sentence or two the user reads.</param>
public sealed record CleanupFailure(CleanupEntry Entry, string Message);
