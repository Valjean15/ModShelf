namespace ModShelf.Application.Installed.Ports;

/// <summary>
/// A game installed from a store, as the store names it.
/// </summary>
/// <param name="Store">The store's ID, such as "steam".</param>
/// <param name="Id">The game's ID in the store, such as Steam's app ID "489830".</param>
/// <param name="Name">Its name in the store, such as "The Elder Scrolls V: Skyrim Special Edition".</param>
/// <param name="Folder">Where it's installed, as a full path.</param>
public sealed record InstalledGame(string Store, string Id, string Name, string Folder)
{
    /// <summary>
    /// The game's store and ID together, such as "steam/489830", as the settings keep the games picked for it.
    /// </summary>
    public string Key => $"{Store}/{Id}";
}
