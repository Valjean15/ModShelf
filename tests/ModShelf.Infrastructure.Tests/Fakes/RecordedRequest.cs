namespace ModShelf.Infrastructure.Tests.Fakes;

/// <summary>
/// The parts of a request the tests look at.
/// </summary>
internal sealed record RecordedRequest(string? Range, string? Authorization, string UserAgent);
