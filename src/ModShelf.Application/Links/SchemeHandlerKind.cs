namespace ModShelf.Application.Links;

/// <summary>
/// Which app the system opens a scheme's links with.
/// </summary>
public enum SchemeHandlerKind
{
    /// <summary>No app: the system doesn't know what to open them with.</summary>
    None,

    /// <summary>ModShelf.</summary>
    ModShelf,

    /// <summary>Another app, such as Vortex or Mod Organizer 2.</summary>
    OtherApp,

    /// <summary>
    /// This ModShelf can't be registered here, such as on macOS outside an installed app, or with no desktop app to open
    /// them with.
    /// </summary>
    Unavailable,
}
