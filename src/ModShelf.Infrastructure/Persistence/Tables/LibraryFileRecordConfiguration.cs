using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// Maps <see cref="LibraryFileRecord"/> to the <c>LibraryFiles</c> table: a file is in the library once per mod.
/// </summary>
/// <remarks>The category is stored as its number, which the domain promises never to renumber.</remarks>
internal sealed class LibraryFileRecordConfiguration : IEntityTypeConfiguration<LibraryFileRecord>
{
    public void Configure(EntityTypeBuilder<LibraryFileRecord> builder)
    {
        builder.ToTable("LibraryFiles");

        builder.HasKey(record => record.Sequence);
        builder.Property(record => record.Sequence).ValueGeneratedOnAdd();

        builder.HasIndex(record => new { record.ModSequence, record.FileId }).IsUnique();
    }
}
