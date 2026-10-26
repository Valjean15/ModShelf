using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// The update feed, reaching back 28 days like Nexus Mods'. Of its game's mods, 101 got a new file 2 hours ago and 102
/// one 3 days ago. The pages of 105 and 103 changed 30 minutes and an hour ago, their files more than a year ago; and
/// 104's page changed 20 days ago, with no word on its files. Another game, "busy", has two mods with new files an hour
/// and two hours ago. The game "locked" needs an account, and "broken" fails.
/// </summary>
internal sealed partial class FakeCatalogSite : IUpdateFeed
{
    public TimeSpan Reach => TimeSpan.FromDays(28);

    /// <summary>
    /// The time the last question about updates was about.
    /// </summary>
    public DateTimeOffset? LastSince { get; private set; }

    public Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        LastSince = since;
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        switch (game.Key)
        {
            case "broken":
                throw new HttpRequestException("Service unavailable.");
            case "locked":
                throw new AuthenticationFailedException("The site wants an account first.");
            case "game" or "busy" when game.Source == SourceId:
                break;
            default:
                throw new ArgumentException($"The site has no game '{game.Key}'.", nameof(game));
        }

        if (since < now - Reach - TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(since), "The site lists the mods updated in the last 28 days only.");
        }

        ModUpdate[] updates = game.Key == "busy"
            ? [Updated(game, "201", now.AddHours(-1), now.AddHours(-1)), Updated(game, "202", now.AddHours(-2), now.AddHours(-2))]
            :
            [
                Updated(game, "101", now.AddHours(-2), now.AddHours(-2)),
                Updated(game, "102", now.AddDays(-3), now.AddDays(-3)),
                Updated(game, "103", now.AddHours(-1), now.AddDays(-400)),
                Updated(game, "104", now.AddDays(-20), null),
                Updated(game, "105", now.AddMinutes(-30), now.AddDays(-500)),
            ];
        return Task.FromResult<IReadOnlyList<ModUpdate>>([.. updates.Where(update => update.LastActivityAt >= since)]);
    }

    private static ModUpdate Updated(GameRef game, string mod, DateTimeOffset activity, DateTimeOffset? files)
        => new(new ModRef(game, mod), activity, files, new Uri($"https://{Host}/{game.Key}/mods/{mod}"));
}
