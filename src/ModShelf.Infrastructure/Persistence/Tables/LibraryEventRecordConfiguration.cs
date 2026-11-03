using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModShelf.Infrastructure.Persistence.Tables;

internal sealed class LibraryEventRecordConfiguration : IEntityTypeConfiguration<LibraryEventRecord>
{
    public void Configure(EntityTypeBuilder<LibraryEventRecord> builder)
    {
        builder.ToTable("LibraryEvents");

        builder.HasKey(record => record.Sequence);

        // In ticks of UTC, which SQLite orders and compares; DateTimeOffset as text it can't.
        builder.Property(record => record.At)
            .HasConversion(at => at.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

        // By name, so a kind added later doesn't change what the rows say.
        builder.Property(record => record.Kind).HasConversion<string>();

        builder.HasIndex(record => record.At);
    }
}
