using System.Net;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A page the <see cref="ScriptedBrowser"/> loaded.
/// </summary>
/// <param name="EndedAt">Where the redirects ended. Secret when it's the app's loopback address with a code in it.</param>
internal sealed record BrowserVisit(Uri EndedAt, HttpStatusCode Status, string Content);
