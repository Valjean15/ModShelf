namespace ModShelf.Application.Links;

/// <summary>
/// Which app the system opens a scheme's links with, and which it would open them with if ModShelf gave them back.
/// </summary>
/// <param name="Kind">Which app opens them.</param>
/// <param name="OtherApp">
/// For <see cref="SchemeHandlerKind.OtherApp"/>, that app's name, such as <c>Vortex</c>, when the system tells.
/// </param>
/// <param name="PreviousApp">
/// For <see cref="SchemeHandlerKind.ModShelf"/>, the name of the app that opens them again once ModShelf gives them
/// back; null when none would, or the system doesn't tell.
/// </param>
public sealed record SchemeState(SchemeHandlerKind Kind, string? OtherApp = null, string? PreviousApp = null)
{
    /// <summary>
    /// No app opens them.
    /// </summary>
    public static SchemeState None { get; } = new(SchemeHandlerKind.None);

    /// <summary>
    /// This ModShelf can't be registered here.
    /// </summary>
    public static SchemeState Unavailable { get; } = new(SchemeHandlerKind.Unavailable);
}
