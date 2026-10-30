namespace ModShelf.Sources.Abstractions.Links;

/// <summary>
/// A site whose website hands downloads over to a mod manager with links of a scheme of its own, such as Nexus Mods'
/// <c>nxm://</c> links from its "mod manager download" buttons. ModShelf can ask the system to open them with it.
/// </summary>
/// <remarks>
/// The links themselves are recognized as any other (<see cref="ILinkRecognizer"/>), and complete a handoff
/// (<see cref="RecognizedLink.IsHandoff"/>).
/// </remarks>
public interface IHandoffScheme : IModSource
{
    /// <summary>
    /// The scheme, such as <c>nxm</c>: lowercase, without <c>://</c>.
    /// </summary>
    string Scheme { get; }
}
