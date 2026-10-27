using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// Maps <see cref="LibraryModRecord"/> to the <c>LibraryMods</c> table, one row per mod, and its files to
/// <c>LibraryFiles</c>, which go with their mod when it's deleted.
/// </summary>
/// <remarks>
/// Text columns compare case-sensitively, SQLite's default, like the download jobs' references.
/// </remarks>
internal sealed class LibraryModRecordConfiguration : IEntityTypeConfiguration<LibraryModRecord>
{
    public void Configure(EntityTypeBuilder<LibraryModRecord> builder)
    {
        builder.ToTable("LibraryMods");

        builder.HasKey(record => record.Sequence);
        builder.Property(record => record.Sequence).ValueGeneratedOnAdd();

        builder.HasIndex(record => new { record.SourceId, record.GameKey, record.ModId }).IsUnique();

        builder.HasMany(record => record.Files)
            .WithOne()
            .HasForeignKey(file => file.ModSequence)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
