namespace ModShelf.Application.Installed.Ports;

/// <summary>
/// A store found on this computer, such as Steam.
/// </summary>
/// <param name="Id">The store's ID, such as "steam", as its games say it.</param>
/// <param name="Name">Its name, such as "Steam".</param>
/// <param name="Folder">Where it was found: its own folder, as a full path.</param>
public sealed record GameStore(string Id, string Name, string Folder);
