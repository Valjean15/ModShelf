using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using ModShelf.Application.Accounts;
using ModShelf.Application.Library;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Persistence;

namespace ModShelf.Infrastructure.Tests.Persistence;

/// <summary>
/// 1.0.0 opens the library database of every version released before it, 0.9.0 to 0.9.5, as it shipped: the linked
/// account, the download queue, and the library's mods with their files, updates, from 0.9.2 their files' hashes, and
/// from 0.9.3 pins, notes, skipped updates and the history, all kept, and the database then works as 1.0.0's.
/// </summary>
public sealed class ReleasedDatabasesTests
{
    private static readonly ModRef Smapi = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");

    public static TheoryData<string, string, bool, bool> Releases => new()
    {
        // The release, the last migration it shipped with, and whether it kept files' hashes, and pins and notes.
        { "0.9.0 and 0.9.1", "20261029231706_AddNeedsFullCheck", false, false },
        { "0.9.2", "20261102113041_AddFileHashes", true, false },
        { "0.9.3 to 0.9.5", "20261103171052_AddPinsAndNotes", true, true },
    };

    [Theory]
    [MemberData(nameof(Releases))]
    public async Task KeepsEverythingAReleasedVersionKept(string release, string migration, bool hashes, bool pins)
    {
        Assert.NotEmpty(release);
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        await using TestLibraryDatabase database = new();
        await using (ModShelfDbContext context = await database.CreateContextAsync())
        {
            await context.GetService<IMigrator>().MigrateAsync(migration, cancellation);

            // What that release wrote, each part in its own columns; no value comes from outside the test.
            string rows =
                "INSERT INTO LinkedAccounts (SourceId, Kind, DisplayName, AvatarUrl, Tier, LinkedAt) "
                + "VALUES ('nexus', 1, 'Kestrel', 'https://avatars.nexusmods.com/1/kestrel.png', 'Premium', '2026-10-28 09:00:00+00:00');"
                + (pins
                    ? "INSERT INTO LibraryMods (SourceId, GameKey, ModId, Name, Author, PageUrl, IsWatched, AddedAt, CheckedAt, NeedsFullCheck, IsPinned, Note) "
                        + "VALUES ('nexus', 'stardewvalley', '2400', 'SMAPI', 'Pathoschild', 'https://www.nexusmods.com/stardewvalley/mods/2400', 1, '2026-10-27 12:00:00+00:00', '2026-11-01 06:00:00+00:00', 0, 1, 'Keep 4.5.2.');"
                    : "INSERT INTO LibraryMods (SourceId, GameKey, ModId, Name, Author, PageUrl, IsWatched, AddedAt, CheckedAt, NeedsFullCheck) "
                        + "VALUES ('nexus', 'stardewvalley', '2400', 'SMAPI', 'Pathoschild', 'https://www.nexusmods.com/stardewvalley/mods/2400', 1, '2026-10-27 12:00:00+00:00', '2026-11-01 06:00:00+00:00', 0);")
                + (hashes
                    ? "INSERT INTO LibraryFiles (ModSequence, FileId, Name, VersionLabel, Category, UploadedAt, RelativePath, SizeInBytes, DownloadedAt, Sha256) "
                        + "VALUES (1, '160380', 'SMAPI 4.5.2', '4.5.2', 1, '2026-10-20 12:00:00+00:00', 'nexus/stardewvalley/SMAPI (2400)/SMAPI 4.5.2.zip', 41889142, '2026-10-27 12:00:00+00:00', '"
                        + new string('a', 64) + "');"
                    : "INSERT INTO LibraryFiles (ModSequence, FileId, Name, VersionLabel, Category, UploadedAt, RelativePath, SizeInBytes, DownloadedAt) "
                        + "VALUES (1, '160380', 'SMAPI 4.5.2', '4.5.2', 1, '2026-10-20 12:00:00+00:00', 'nexus/stardewvalley/SMAPI (2400)/SMAPI 4.5.2.zip', 41889142, '2026-10-27 12:00:00+00:00');")
                + "INSERT INTO LibraryUpdates (ModSequence, Position, FileId, Name, FileName, VersionLabel, Category, UploadedAt, ReplacesFileId, FoundAt, PageUrl) "
                + "VALUES (1, 0, '161000', 'SMAPI 4.5.3', 'SMAPI 4.5.3.zip', '4.5.3', 1, '2026-10-31 12:00:00+00:00', '160380', '2026-11-01 06:00:00+00:00', 'https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=161000');"
                + (pins
                    ? "INSERT INTO SkippedUpdates (ModSequence, FileId) VALUES (1, '161000');"
                        + "INSERT INTO LibraryEvents (At, Kind, SourceId, GameKey, ModId, ModName, FileId, FileName, VersionLabel, RelativePath, ByRule) "
                        + "VALUES (639286992000000000, 'Downloaded', 'nexus', 'stardewvalley', '2400', 'SMAPI', '160380', 'SMAPI 4.5.2', '4.5.2', 'nexus/stardewvalley/SMAPI (2400)/SMAPI 4.5.2.zip', 0);"
                    : string.Empty);
            await context.Database.ExecuteSqlRawAsync(rows, cancellation);
        }

        DownloadJob job = DownloadJob.Create(new ModFileRef(Smapi, "161000"), Path.GetDirectoryName(database.DatabasePath)!, "SMAPI 4.5.3.zip", DateTimeOffset.UnixEpoch);
        await database.Repository.AddAsync(job, cancellation);

        await database.StartAsync();

        LinkedAccount account = Assert.Single(await database.LinkedAccounts.ListAsync(cancellation));
        Assert.Equal(("Kestrel", "Premium"), (account.Profile.DisplayName, account.Profile.Tier));
        Assert.Equal(job.Id, Assert.Single(await database.Repository.ListAsync(cancellation)).Id);
        LibraryMod mod = Assert.Single(await database.Library.ListAsync(cancellation));
        LibraryFile file = Assert.Single(mod.Files);
        Assert.Equal(("SMAPI", "Pathoschild", true), (mod.Name, mod.Author, mod.IsWatched));
        Assert.Equal(("160380", 41_889_142L, hashes ? new string('a', 64) : null), (file.File.Id, file.SizeInBytes, file.Sha256));
        Assert.Equal("161000", Assert.Single(mod.Updates).File.Id);
        Assert.Equal((pins, pins ? "Keep 4.5.2." : null, pins ? 1 : 0), (mod.IsPinned, mod.Note, mod.SkippedUpdates.Count));
        if (pins)
        {
            // From 0.9.3, the library's history, the file's download in it.
            LibraryEvent downloaded = Assert.Single(await database.History.ListAsync(null, 10, cancellation));
            Assert.Equal((new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero), LibraryEventKind.Downloaded, Smapi, "160380", "4.5.2"), (downloaded.At, downloaded.Kind, downloaded.Mod, downloaded.File?.Id, downloaded.VersionLabel));
        }

        await using (ModShelfDbContext context = await database.CreateContextAsync())
        {
            Assert.Equal((long)context.Database.GetMigrations().Count(), await database.QueryAsync("SELECT COUNT(*) FROM __EFMigrationsHistory"));
        }

        // And it works as 1.0.0's: a note kept, a removal in the history, and the mod back as it was.
        LibraryChange noted = await database.Library.UpdateAsync(Smapi, stored => stored!.WithNote("From 1.0.0."), cancellation);
        await database.History.AddAsync([new LibraryEvent(DateTimeOffset.UnixEpoch.AddYears(57), LibraryEventKind.Removed, Smapi, "SMAPI")], DateTimeOffset.UnixEpoch, cancellation);
        await database.Library.UpdateAsync(Smapi, _ => noted.Before, cancellation);
        Assert.Equal(mod.Note, Assert.Single(await database.Library.ListAsync(cancellation)).Note);
        Assert.Equal(pins ? 2 : 1, (await database.History.ListAsync(null, 10, cancellation)).Count);
    }
}
