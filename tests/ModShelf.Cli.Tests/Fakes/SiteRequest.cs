using System.Net;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A request the <see cref="FakeAccountSite"/> answered.
/// </summary>
/// <param name="Credential">The <c>Authorization</c> header, or else the API key header; null when it had neither.</param>
internal sealed record SiteRequest(string Method, string Path, string? Credential, HttpStatusCode Status);
