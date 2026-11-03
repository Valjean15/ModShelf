using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModShelf.Infrastructure.Persistence.Tables;

/// <summary>
/// Maps <see cref="SkippedUpdateRecord"/> to the <c>SkippedUpdates</c> table: a mod skips a file once.
/// </summary>
internal sealed class SkippedUpdateRecordConfiguration : IEntityTypeConfiguration<SkippedUpdateRecord>
{
    public void Configure(EntityTypeBuilder<SkippedUpdateRecord> builder)
    {
        builder.ToTable("SkippedUpdates");

        builder.HasKey(record => new { record.ModSequence, record.FileId });
    }
}
