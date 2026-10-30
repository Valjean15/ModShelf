namespace ModShelf.Application.Links;

/// <summary>
/// Which app the system opens a scheme's links with, and which it would open them with if ModShelf gave them back.
/// </summary>
/// <param name="Kind">Which app opens them.</param>
/// <param name="OtherApp">
/// For <see cref="SchemeHandlerKind.OtherApp"/>, that app's name, such as <c>Vortex</c>, when the system tells.
/// </param>
/// <param name="PreviousApp">
/// For <see cref="SchemeHandlerKind.ModShelf"/>, or with <paramref name="IsModShelfBehind"/>, the name of the app that
/// opens them again once ModShelf gives them back; null when none would, or the system doesn't tell.
/// </param>
/// <param name="IsModShelfBehind">
/// For <see cref="SchemeHandlerKind.OtherApp"/>: ModShelf is registered for them, but the system prefers that app, one
/// the user chose in its own settings. Giving the links back still removes ModShelf's registration.
/// </param>
public sealed record SchemeState(SchemeHandlerKind Kind, string? OtherApp = null, string? PreviousApp = null, bool IsModShelfBehind = false)
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
