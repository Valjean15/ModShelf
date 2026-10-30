namespace ModShelf.Platform.Links;

/// <summary>
/// What the system starts when it opens a link with ModShelf: the desktop app, with the arguments that keep it on this
/// library, then the link.
/// </summary>
/// <param name="DesktopApp">The desktop app's executable, an absolute path. Registering needs it to exist.</param>
/// <param name="Arguments">
/// The arguments before the link, such as <c>--ModShelf:DatabasePath=…</c> when the library database isn't in its
/// default place, so that the app the link starts, or the one it reaches, is this library's.
/// </param>
public sealed record SchemeHandlerOptions(string DesktopApp, IReadOnlyList<string> Arguments);
