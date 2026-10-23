namespace ModShelf.Infrastructure.Tests.Fakes;

/// <summary>
/// The parts of a request the tests look at.
/// </summary>
/// <param name="Headers">Every request header, as name and comma-joined values.</param>
internal sealed record RecordedRequest(
    string? Range,
    string? Authorization,
    string UserAgent,
    Uri? Uri = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Body = null);
