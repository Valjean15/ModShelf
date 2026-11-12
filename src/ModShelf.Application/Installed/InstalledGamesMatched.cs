using ModShelf.Application.Installed.Ports;

namespace ModShelf.Application.Installed;

/// <summary>
/// The stores found on this computer, and the games installed from them, each with its games on the sites.
/// </summary>
public sealed record InstalledGamesMatched(IReadOnlyList<GameStore> Stores, IReadOnlyList<InstalledGameMatch> Games);
