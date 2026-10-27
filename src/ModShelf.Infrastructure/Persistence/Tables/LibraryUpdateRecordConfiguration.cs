using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// Maps <see cref="LibraryUpdateRecord"/> to the <c>LibraryUpdates</c> table: a file is an update of a mod once.
/// </summary>
/// <remarks>The category is stored as its number, which the domain promises never to renumber.</remarks>
internal sealed class LibraryUpdateRecordConfiguration : IEntityTypeConfiguration<LibraryUpdateRecord>
{
    public void Configure(EntityTypeBuilder<LibraryUpdateRecord> builder)
    {
        builder.ToTable("LibraryUpdates");

        builder.HasKey(record => record.Sequence);
        builder.Property(record => record.Sequence).ValueGeneratedOnAdd();

        builder.HasIndex(record => new { record.ModSequence, record.FileId }).IsUnique();
    }
}
