using ModShelf.Application.Installed;
using ModShelf.Application.Installed.Ports;

namespace ModShelf.Cli.Common;

/// <summary>
/// The stores found on this computer, the installed games kept with their games on the sites, and the sites whose games
/// couldn't be read.
/// </summary>
internal sealed record InstalledGameListing(IReadOnlyList<GameStore> Stores, IReadOnlyList<InstalledGameMatch> Games, IReadOnlyList<UnreadSite> Unread);
